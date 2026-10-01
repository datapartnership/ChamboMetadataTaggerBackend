using System.Text;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using System.Reflection;
using MetadataTagging.Authentication;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.Services;
using MetadataTagging.Swagger;
using MetadataTagger.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection(AuthenticationOptions.Section));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.Configure<MetadataTagger.Options.CorsOptions>(builder.Configuration.GetSection(MetadataTagger.Options.CorsOptions.Section));
builder.Services.Configure<EntraIdOptions>(builder.Configuration.GetSection(EntraIdOptions.Section));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));
builder.Services.Configure<DefaultAdminSettings>(builder.Configuration.GetSection(DefaultAdminSettings.Section));
builder.Services.Configure<DefaultTaggerSettings>(builder.Configuration.GetSection(DefaultTaggerSettings.Section));
builder.Services.Configure<DefaultSupervisorSettings>(builder.Configuration.GetSection(DefaultSupervisorSettings.Section));

// ── Catalog API options (ported from the LRLL merge) ─────────────────────────
builder.Services.Configure<CatalogStorageOptions>(builder.Configuration.GetSection(CatalogStorageOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

var dbOptions = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()
    ?? new DatabaseOptions();

// Build the NpgsqlDataSource once at startup so the same instance is reused across
// all DbContext instantiations. Creating a new data source inside the AddDbContext
// lambda causes EF Core to see different options on every request and allocate a new
// internal IServiceProvider each time, triggering ManyServiceProvidersCreatedWarning.
Npgsql.NpgsqlDataSource? npgsqlDataSource = null;
if (dbOptions.UseManagedIdentity)
{
    var connectionString = $"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Name};Username={dbOptions.Username};Ssl Mode=Require;Trust Server Certificate=true;";
    var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UsePeriodicPasswordProvider(async (_, ct) =>
    {
        var credentialOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrEmpty(dbOptions.ManagedIdentityClientId))
            credentialOptions.ManagedIdentityClientId = dbOptions.ManagedIdentityClientId;
        var credential = new DefaultAzureCredential(credentialOptions);
        var token = await credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(new[] { "https://ossrdbms-aad.database.windows.net/.default" }), ct);
        return token.Token;
    }, TimeSpan.FromMinutes(55), TimeSpan.FromSeconds(0));
    npgsqlDataSource = dataSourceBuilder.Build();
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (npgsqlDataSource != null)
        options.UseNpgsql(npgsqlDataSource);
    else
        options.UseNpgsql($"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Name};Username={dbOptions.Username};Password={dbOptions.Password}");
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEntraUserProvisioningService, EntraUserProvisioningService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<ISupervisorService, SupervisorService>();
builder.Services.AddScoped<TaggingImportService>();

// ── Catalog API services (ported from the LRLL merge) ────────────────────────
builder.Services.AddSingleton<CroissantMetadataParser>();
builder.Services.AddScoped<CatalogTagPublisher>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CollectionQueryService>();
builder.Services.AddScoped<CollectionBundleService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddSingleton<CroissantBuilder>();
builder.Services.AddSingleton<TextChunkService>();

// ── Catalog file storage (local disk, S3-compatible, or Azure Blob Storage) ──
// One bucket/container laid out as Provider/CollectionFolder, shared by the tagging
// workflow and the catalog API so tagger BlobNames and catalog ContentUrls are the same keys.
var catalogStorageOptions = builder.Configuration.GetSection(CatalogStorageOptions.SectionName).Get<CatalogStorageOptions>()
    ?? new CatalogStorageOptions();
if (catalogStorageOptions.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorageService, S3FileStorageService>();
else if (catalogStorageOptions.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorageService, AzureBlobStorageService>();
else
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

var authOptions = builder.Configuration.GetSection(AuthenticationOptions.Section).Get<AuthenticationOptions>()
    ?? new AuthenticationOptions();
if (!authOptions.IsKnownMethod)
{
    throw new InvalidOperationException(
        $"Authentication:Method must be one of '{AuthenticationMethods.Jwt}', '{AuthenticationMethods.EntraId}', or '{AuthenticationMethods.Keycloak}' (got '{authOptions.Method}').");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>();
var entraIdOptions = builder.Configuration.GetSection(EntraIdOptions.Section).Get<EntraIdOptions>()
    ?? new EntraIdOptions();
var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
    ?? new KeycloakOptions();

const string LocalAuthScheme = "Local";
const string EntraIdAuthScheme = "EntraId";
const string KeycloakAuthScheme = "Keycloak";

var recognizedRoles = new HashSet<string>(
    [UserRoles.Admin, UserRoles.Supervisor, UserRoles.Tagger],
    StringComparer.OrdinalIgnoreCase);

// Only validate / register the provider selected by Authentication:Method. Nested
// EntraId / Keycloak / JwtSettings sections can stay populated for other environments
// without accidentally enabling those schemes.
if (authOptions.IsJwt)
{
    if (jwtOptions == null || string.IsNullOrEmpty(jwtOptions.SecretKey))
    {
        throw new InvalidOperationException(
            "JwtSettings:SecretKey is required when Authentication:Method is Jwt.");
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = LocalAuthScheme;
        options.DefaultChallengeScheme = LocalAuthScheme;
    })
    .AddJwtBearer(LocalAuthScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey))
        };
    });
}
else if (authOptions.IsEntraId)
{
    if (!entraIdOptions.IsConfigured)
    {
        throw new InvalidOperationException(
            "EntraId:TenantId and EntraId:ClientId are required when Authentication:Method is EntraId.");
    }

    var invalidGroupRoleMapping = entraIdOptions.GroupRoleMappings.FirstOrDefault(mapping =>
        !Guid.TryParse(mapping.GroupId, out _) || !recognizedRoles.Contains(mapping.Role));
    if (invalidGroupRoleMapping != null)
    {
        throw new InvalidOperationException(
            "Each EntraId:GroupRoleMappings entry must contain a valid group object ID and a role of Admin, Supervisor, or Tagger.");
    }

    var conflictingGroupRoleMapping = entraIdOptions.GroupRoleMappings
        .GroupBy(mapping => mapping.GroupId, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group
            .Select(mapping => mapping.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any());
    if (conflictingGroupRoleMapping != null)
    {
        throw new InvalidOperationException(
            $"Entra group '{conflictingGroupRoleMapping.Key}' is mapped to more than one application role.");
    }

    var authenticationBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = EntraIdAuthScheme;
        options.DefaultChallengeScheme = EntraIdAuthScheme;
    });

    authenticationBuilder.AddMicrosoftIdentityWebApi(
        builder.Configuration,
        configSectionName: EntraIdOptions.Section,
        jwtBearerScheme: EntraIdAuthScheme);

    // Map Entra App Roles onto ClaimTypes.Role and JIT-provision/sync the local
    // user record so downstream controllers see the same claim shape as local logins.
    builder.Services.Configure<JwtBearerOptions>(EntraIdAuthScheme, options =>
    {
        options.TokenValidationParameters.RoleClaimType = entraIdOptions.RoleClaimType;

        options.Events ??= new JwtBearerEvents();
        var previousOnTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (previousOnTokenValidated != null)
            {
                await previousOnTokenValidated(context);
            }

            await EntraTokenClaimsNormalizer.NormalizeAsync(context, entraIdOptions);
        };
    });
}
else // Keycloak
{
    if (!keycloakOptions.IsConfigured)
    {
        throw new InvalidOperationException(
            "Keycloak:Authority is required when Authentication:Method is Keycloak.");
    }

    var invalidKeycloakRoleMapping = keycloakOptions.RoleMappings.FirstOrDefault(mapping =>
        string.IsNullOrWhiteSpace(mapping.KeycloakRole) || !recognizedRoles.Contains(mapping.Role));
    if (invalidKeycloakRoleMapping != null)
    {
        throw new InvalidOperationException(
            "Each Keycloak:RoleMappings entry must contain a Keycloak client role name and a role of Admin, Supervisor, or Tagger.");
    }

    var conflictingKeycloakRoleMapping = keycloakOptions.RoleMappings
        .GroupBy(mapping => mapping.KeycloakRole, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group
            .Select(mapping => mapping.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any());
    if (conflictingKeycloakRoleMapping != null)
    {
        throw new InvalidOperationException(
            $"Keycloak role '{conflictingKeycloakRoleMapping.Key}' is mapped to more than one application role.");
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = KeycloakAuthScheme;
        options.DefaultChallengeScheme = KeycloakAuthScheme;
    })
    .AddJwtBearer(KeycloakAuthScheme, options =>
    {
        options.Authority = keycloakOptions.Authority;
        options.Audience = keycloakOptions.Audience;
        options.RequireHttpsMetadata = keycloakOptions.RequireHttpsMetadata;
        options.TokenValidationParameters.ValidateIssuer = true;

        // Map Keycloak client roles onto ClaimTypes.Role and JIT-provision/sync the
        // local user record so downstream controllers see the same claim shape as
        // local and Entra ID logins.
        options.Events ??= new JwtBearerEvents();
        var previousOnTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (previousOnTokenValidated != null)
            {
                await previousOnTokenValidated(context);
            }

            await KeycloakTokenClaimsNormalizer.NormalizeAsync(context, keycloakOptions);
        };
    });
}

// ── Authorization policies ────────────────────────────────────────────────────
// The catalog API's "Read"/"Write" policies (ported from LRLL, which originally
// gated them on Azure AD groups / Keycloak roles) are re-mapped onto Chambo's own
// Admin/Supervisor/Tagger role model: any authenticated (and therefore JIT-verified
// or locally provisioned) user can Read; only Admins can Write.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Read", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Write", policy => policy.RequireRole(UserRoles.Admin));
});

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Metadata Tagging API",
        Version = "v1",
        Description = "API for managing file metadata tagging with role-based access control"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Entra ID OAuth2 Authorization Code + PKCE (ported from the LRLL merge)
    if (authOptions.IsEntraId)
    {
        c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{entraIdOptions.Instance.TrimEnd('/')}/{entraIdOptions.TenantId}/oauth2/v2.0/authorize"),
                    TokenUrl = new Uri($"{entraIdOptions.Instance.TrimEnd('/')}/{entraIdOptions.TenantId}/oauth2/v2.0/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        [$"{entraIdOptions.Audience}/user_impersonation"] = "Access the Metadata Tagging API"
                    }
                }
            }
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                },
                [$"{entraIdOptions.Audience}/user_impersonation"]
            }
        });
    }

    // Keycloak OAuth2 Authorization Code + PKCE (ported from the LRLL merge)
    if (authOptions.IsKeycloak)
    {
        c.AddSecurityDefinition("keycloak-oauth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri(keycloakOptions.AuthorizationUrl),
                    TokenUrl = new Uri(keycloakOptions.TokenUrl),
                    Scopes = new Dictionary<string, string>
                    {
                        ["openid"] = "OpenID Connect",
                        ["profile"] = "User profile"
                    }
                }
            }
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "keycloak-oauth2" }
                },
                ["openid"]
            }
        });
    }

    // Include XML comments from the project (controllers + DTOs) -- ported from the LRLL merge.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);

    // Inject realistic Croissant JSON-LD examples into Swagger UI for the catalog endpoints.
    c.OperationFilter<CroissantExampleFilter>();
});

var corsOptions = builder.Configuration.GetSection(MetadataTagger.Options.CorsOptions.Section).Get<MetadataTagger.Options.CorsOptions>()
    ?? new MetadataTagger.Options.CorsOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        var origins = corsOptions.AllowedOrigins.Select(o => o.TrimEnd('/')).ToArray();
        if (origins.Contains("*"))
            policy.AllowAnyOrigin();
        else if (origins.Length > 0)
            policy.WithOrigins(origins);
        // else: no origins configured — deny all (no CORS headers sent)

        policy.AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Schema is managed via EF Core Migrations (ported from the LRLL merge), replacing
    // the previous EnsureCreated()/manual column-patching approach. See the "InitialBaseline"
    // migration for notes on applying this to an already-provisioned production database.
    if (dbOptions.ResetOnStartup)
    {
        logger.LogWarning("Database:ResetOnStartup is true: dropping all tables and rebuilding the schema. " +
                          "All data is lost on every restart until this is set back to false.");
        await DatabaseReset.DropAllTablesAsync(dbContext);
    }
    await dbContext.Database.MigrateAsync();

    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        if (canConnect)
            logger.LogInformation("Database connected successfully ({Host}/{Database}).", dbOptions.Host, dbOptions.Name);
        else
            logger.LogWarning("Database connection check failed ({Host}/{Database}).", dbOptions.Host, dbOptions.Name);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to connect to database ({Host}/{Database}).", dbOptions.Host, dbOptions.Name);
    }

    // Create default Admin user
    var adminSettings = scope.ServiceProvider.GetRequiredService<IOptions<DefaultAdminSettings>>().Value;
    if (!string.IsNullOrEmpty(adminSettings?.Email) &&
        !string.IsNullOrEmpty(adminSettings.Username) &&
        !string.IsNullOrEmpty(adminSettings.Password))
    {
        var existingAdmin = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == adminSettings.Email);
        if (existingAdmin == null)
        {
            var adminUser = new User
            {
                Username = adminSettings.Username,
                Email = adminSettings.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminSettings.Password),
                Role = UserRoles.Admin,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            dbContext.Users.Add(adminUser);
            await dbContext.SaveChangesAsync();
        }
    }

    // Create default Tagger user
    var taggerSettings = scope.ServiceProvider.GetRequiredService<IOptions<DefaultTaggerSettings>>().Value;
    if (!string.IsNullOrEmpty(taggerSettings?.Email) &&
        !string.IsNullOrEmpty(taggerSettings.Username) &&
        !string.IsNullOrEmpty(taggerSettings.Password))
    {
        var existingTagger = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == taggerSettings.Email);
        if (existingTagger == null)
        {
            var taggerUser = new User
            {
                Username = taggerSettings.Username,
                Email = taggerSettings.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(taggerSettings.Password),
                Role = UserRoles.Tagger,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            dbContext.Users.Add(taggerUser);
            await dbContext.SaveChangesAsync();
        }
    }

    // Create default Supervisor user
    var supervisorSettings = scope.ServiceProvider.GetRequiredService<IOptions<DefaultSupervisorSettings>>().Value;
    if (!string.IsNullOrEmpty(supervisorSettings?.Email) &&
        !string.IsNullOrEmpty(supervisorSettings.Username) &&
        !string.IsNullOrEmpty(supervisorSettings.Password))
    {
        var existingSupervisor = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == supervisorSettings.Email);
        if (existingSupervisor == null)
        {
            var supervisorUser = new User
            {
                Username = supervisorSettings.Username,
                Email = supervisorSettings.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(supervisorSettings.Password),
                Role = UserRoles.Supervisor,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            dbContext.Users.Add(supervisorUser);
            await dbContext.SaveChangesAsync();
        }
    }

    if (catalogStorageOptions.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
        && scope.ServiceProvider.GetRequiredService<IFileStorageService>() is AzureBlobStorageService azureStorage)
    {
        try
        {
            await azureStorage.LogPrefixAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list Azure catalog storage prefix '{Prefix}'.",
                string.IsNullOrEmpty(catalogStorageOptions.Azure.Prefix) ? "(container root)" : catalogStorageOptions.Azure.Prefix);
        }
    }
}


// Security response headers, applied to every response (including CORS preflights and errors).
// HSTS is set manually rather than via UseHsts(): TLS terminates upstream on App Service.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    // Swagger UI (Development only) needs inline scripts/styles, so skip the strict CSP there.
    if (!context.Request.Path.StartsWithSegments("/swagger"))
        headers["Content-Security-Policy"] = "default-src 'self';";
    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["X-XSS-Protection"] = "1; mode=block";
    headers["Referrer-Policy"] = "no-referrer-when-downgrade";
    await next();
});

//let's serve the fronted in another service
//app.UseStaticFiles();

// Swagger is Development-only: its UI is incompatible with the strict CSP above.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metadata Tagging API v1");
        c.DefaultModelsExpandDepth(-1); // hide schema section by default
        c.DisplayRequestDuration();
        if (authOptions.IsEntraId)
        {
            c.OAuthClientId(entraIdOptions.ClientId);
            c.OAuthScopes($"{entraIdOptions.Audience}/user_impersonation");
            c.OAuthUsePkce();
        }
        if (authOptions.IsKeycloak)
        {
            // Allows Swagger UI to pick up the Keycloak client when 'keycloak-oauth2' is selected.
            c.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
            {
                ["kc_client_id"] = keycloakOptions.ClientId
            });
        }
    });
}

app.UseCors("AllowAll");

//NOT NEEDED ON APP SERVICE
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint for readiness probe
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
