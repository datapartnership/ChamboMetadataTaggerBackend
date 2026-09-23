using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Azure.Core;
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

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<S3StorageOptions>(builder.Configuration.GetSection("Storage:S3"));
builder.Services.Configure<AzureStorageOptions>(builder.Configuration.GetSection("Storage:AzureStorage"));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.Configure<MetadataTagger.Options.CorsOptions>(builder.Configuration.GetSection(MetadataTagger.Options.CorsOptions.Section));
builder.Services.Configure<EntraIdOptions>(builder.Configuration.GetSection(EntraIdOptions.Section));
builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection(KeycloakOptions.SectionName));
builder.Services.Configure<DefaultAdminSettings>(builder.Configuration.GetSection(DefaultAdminSettings.Section));
builder.Services.Configure<DefaultTaggerSettings>(builder.Configuration.GetSection(DefaultTaggerSettings.Section));
builder.Services.Configure<DefaultSupervisorSettings>(builder.Configuration.GetSection(DefaultSupervisorSettings.Section));

// ── Catalog API options (ported from the LRLL merge) ─────────────────────────
builder.Services.Configure<CatalogStorageOptions>(builder.Configuration.GetSection(CatalogStorageOptions.SectionName));
builder.Services.Configure<CollectionCatalogOptions>(builder.Configuration.GetSection(CollectionCatalogOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

var dbOptions = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()
    ?? new DatabaseOptions();

// Build the NpgsqlDataSource once at startup so the same instance is reused across
// all DbContext instantiations. Creating a new data source inside the AddDbContext
// lambda causes EF Core to see different options on every request and allocate a new
// internal IServiceProvider each time, triggering ManyServiceProvidersCreatedWarning.
Npgsql.NpgsqlDataSource? npgsqlDataSource = null;
if (dbOptions.Provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) && dbOptions.UseManagedIdentity)
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
    if (dbOptions.Provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
    {
        if (npgsqlDataSource != null)
            options.UseNpgsql(npgsqlDataSource);
        else
            options.UseNpgsql($"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Name};Username={dbOptions.Username};Password={dbOptions.Password}");
    }
    else
        options.UseSqlite($"Data Source={dbOptions.DataSource}");
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEntraUserProvisioningService, EntraUserProvisioningService>();
builder.Services.AddScoped<IFileService, FileService>();

var storageOptions = builder.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>()
    ?? new StorageOptions();
if (storageOptions.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<IStorageService, S3StorageService>();
else
    builder.Services.AddScoped<IStorageService, AzureBlobService>();

builder.Services.AddScoped<ISupervisorService, SupervisorService>();

// ── Catalog API services (ported from the LRLL merge) ────────────────────────
builder.Services.AddScoped<FileQueryService>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddSingleton<CroissantBuilder>();
builder.Services.AddSingleton<TextChunkService>();

// Sound (FLAC) services
builder.Services.AddSingleton<SoundMetadataParser>();
builder.Services.AddScoped<SoundSeedService>();
builder.Services.AddScoped<SoundQueryService>();
builder.Services.AddSingleton<SoundCroissantBuilder>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CollectionCatalogService>();

// ── Catalog file storage (local disk, S3-compatible, or Azure Blob Storage) ──
// Deliberately separate from Chambo's own tagging-inbox IStorageService/StorageOptions above.
var catalogStorageOptions = builder.Configuration.GetSection(CatalogStorageOptions.SectionName).Get<CatalogStorageOptions>()
    ?? new CatalogStorageOptions();
if (catalogStorageOptions.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorageService, S3FileStorageService>();
else if (catalogStorageOptions.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorageService, AzureBlobStorageService>();
else
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

// ── Sound storage (same provider, dedicated bucket/container) ────────────────
if (catalogStorageOptions.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddKeyedSingleton<IFileStorageService>("sound", (sp, _) =>
        new S3FileStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.S3.SoundBucketName));
else if (catalogStorageOptions.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddKeyedSingleton<IFileStorageService>("sound", (sp, _) =>
        new AzureBlobStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.Azure.SoundContainerName));
else
    builder.Services.AddKeyedSingleton<IFileStorageService>("sound", (sp, _) =>
        new LocalFileStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.SoundFileStorageRoot));

// ── Collections catalog storage (same provider, dedicated bucket/container) ──
if (catalogStorageOptions.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddKeyedSingleton<IFileStorageService>("collections", (sp, _) =>
        new S3FileStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.S3.CollectionsBucketName));
else if (catalogStorageOptions.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddKeyedSingleton<IFileStorageService>("collections", (sp, _) =>
        new AzureBlobStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.Azure.CollectionsContainerName));
else
    builder.Services.AddKeyedSingleton<IFileStorageService>("collections", (sp, _) =>
        new LocalFileStorageService(sp.GetRequiredService<IOptions<CatalogStorageOptions>>(), catalogStorageOptions.CollectionsFileStorageRoot));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>();
if (jwtOptions == null || string.IsNullOrEmpty(jwtOptions.SecretKey))
{
    throw new InvalidOperationException("JWT SecretKey not configured");
}
var secretKey = jwtOptions.SecretKey;

var entraIdOptions = builder.Configuration.GetSection(EntraIdOptions.Section).Get<EntraIdOptions>()
    ?? new EntraIdOptions();
var keycloakOptions = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
    ?? new KeycloakOptions();

const string LocalAuthScheme = "Local";
const string EntraIdAuthScheme = "EntraId";
const string KeycloakAuthScheme = "Keycloak";
const string SmartAuthScheme = "Smart";

var recognizedRoles = new HashSet<string>(
    [UserRoles.Admin, UserRoles.Supervisor, UserRoles.Tagger],
    StringComparer.OrdinalIgnoreCase);
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

// True when at least one federated identity provider (Entra ID and/or Keycloak) is
// configured alongside the always-available local email/password JWT login.
var federatedAuthConfigured = entraIdOptions.IsConfigured || keycloakOptions.IsConfigured;

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    // When no federated IdP is configured, behave exactly as before: the local JWT
    // scheme is the only (and default) one.
    options.DefaultAuthenticateScheme = federatedAuthConfigured ? SmartAuthScheme : LocalAuthScheme;
    options.DefaultChallengeScheme = federatedAuthConfigured ? SmartAuthScheme : LocalAuthScheme;
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

if (entraIdOptions.IsConfigured)
{
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

if (keycloakOptions.IsConfigured)
{
    authenticationBuilder.AddJwtBearer(KeycloakAuthScheme, options =>
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

if (federatedAuthConfigured)
{
    // Routes each incoming request to the "Local", "EntraId", or "Keycloak" scheme based
    // on the bearer token's issuer, so existing [Authorize(Roles = ...)]/policy attributes
    // (which don't specify a scheme) keep working unchanged for every token type.
    authenticationBuilder.AddPolicyScheme(SmartAuthScheme, "Local, Entra ID, or Keycloak", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorizationHeader = context.Request.Headers.Authorization.ToString();
            if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorizationHeader["Bearer ".Length..].Trim();
                try
                {
                    var issuer = new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;

                    if (entraIdOptions.IsConfigured &&
                        (issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                         issuer.Contains("sts.windows.net", StringComparison.OrdinalIgnoreCase)))
                    {
                        return EntraIdAuthScheme;
                    }

                    if (keycloakOptions.IsConfigured &&
                        issuer.TrimEnd('/').Equals(
                            keycloakOptions.Authority.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        return KeycloakAuthScheme;
                    }
                }
                catch (Exception)
                {
                    // Not a well-formed JWT — fall through to the local scheme, which
                    // will reject it during normal validation.
                }
            }

            return LocalAuthScheme;
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
    if (entraIdOptions.IsConfigured)
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
    if (keycloakOptions.IsConfigured)
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
    await dbContext.Database.MigrateAsync();

    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        if (canConnect)
            logger.LogInformation("Database connected successfully (provider: {Provider}).", dbOptions.Provider);
        else
            logger.LogWarning("Database connection check failed (provider: {Provider}).", dbOptions.Provider);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to connect to database (provider: {Provider}).", dbOptions.Provider);
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

    // ── Rebuild the Collections catalog from the configured Excel workbook (ported from
    // the LRLL merge) ────────────────────────────────────────────────────────────────────
    // A missing/unreachable workbook must not prevent the app from starting.
    var collectionCatalogOptions = scope.ServiceProvider.GetRequiredService<IOptions<CollectionCatalogOptions>>().Value;
    if (collectionCatalogOptions.Enabled)
    {
        try
        {
            var collectionCatalogService = scope.ServiceProvider.GetRequiredService<CollectionCatalogService>();
            var syncResult = await collectionCatalogService.SyncFromBlobAsync();
            if (syncResult.success)
                logger.LogInformation(
                    "Collections catalog synced on startup: {Synced} rows synced, {Skipped} rows skipped.",
                    syncResult.rowsSynced, syncResult.rowsSkipped);
            else
                logger.LogWarning("Collections catalog sync did not complete: {Error}", syncResult.error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync collections catalog on startup.");
        }
    }
}

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

app.UseStaticFiles();

// Swagger is available in all environments (not just Development): the merged app also
// serves the public LRLL catalog API, whose consumers rely on Swagger UI in production
// for the OAuth2 Authorization Code + PKCE sign-in flow. Endpoints still require the
// same authentication/authorization as ordinary requests.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metadata Tagging API v1");
    c.DefaultModelsExpandDepth(-1); // hide schema section by default
    c.DisplayRequestDuration();
    if (entraIdOptions.IsConfigured)
    {
        c.OAuthClientId(entraIdOptions.ClientId);
        c.OAuthScopes($"{entraIdOptions.Audience}/user_impersonation");
        c.OAuthUsePkce();
    }
    if (keycloakOptions.IsConfigured)
    {
        // Allows Swagger UI to pick up the Keycloak client when 'keycloak-oauth2' is selected.
        c.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
        {
            ["kc_client_id"] = keycloakOptions.ClientId
        });
    }
});

app.UseCors("AllowAll");

//NOT NEEDED ON APP SERVICE
//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint for readiness probe
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
