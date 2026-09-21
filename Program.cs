using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MetadataTagging.Authentication;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.Services;
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
builder.Services.Configure<DefaultAdminSettings>(builder.Configuration.GetSection(DefaultAdminSettings.Section));
builder.Services.Configure<DefaultTaggerSettings>(builder.Configuration.GetSection(DefaultTaggerSettings.Section));
builder.Services.Configure<DefaultSupervisorSettings>(builder.Configuration.GetSection(DefaultSupervisorSettings.Section));

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

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>();
if (jwtOptions == null || string.IsNullOrEmpty(jwtOptions.SecretKey))
{
    throw new InvalidOperationException("JWT SecretKey not configured");
}
var secretKey = jwtOptions.SecretKey;

var entraIdOptions = builder.Configuration.GetSection(EntraIdOptions.Section).Get<EntraIdOptions>()
    ?? new EntraIdOptions();

const string LocalAuthScheme = "Local";
const string EntraIdAuthScheme = "EntraId";
const string SmartAuthScheme = "Smart";

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    // When Entra ID isn't configured, behave exactly as before: the local JWT
    // scheme is the only (and default) one.
    options.DefaultAuthenticateScheme = entraIdOptions.IsConfigured ? SmartAuthScheme : LocalAuthScheme;
    options.DefaultChallengeScheme = entraIdOptions.IsConfigured ? SmartAuthScheme : LocalAuthScheme;
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

            await EntraTokenClaimsNormalizer.NormalizeAsync(context, entraIdOptions.RoleClaimType);
        };
    });

    // Routes each incoming request to the "Local" or "EntraId" scheme based on the
    // bearer token's issuer, so existing [Authorize(Roles = ...)] attributes (which
    // don't specify a scheme) keep working unchanged for both token types.
    authenticationBuilder.AddPolicyScheme(SmartAuthScheme, "Local or Entra ID", options =>
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
                    if (issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                        issuer.Contains("sts.windows.net", StringComparison.OrdinalIgnoreCase))
                    {
                        return EntraIdAuthScheme;
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

builder.Services.AddAuthorization();

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
    //dbContext.Database.EnsureDeleted();
    dbContext.Database.EnsureCreated();

    // EnsureCreated() only creates the schema for a brand-new database; it does not
    // alter tables that already exist. Since this project has no EF Core migrations,
    // patch pre-existing "Users" tables (e.g. an already-provisioned dev/prod database)
    // so they pick up the columns added for Entra ID support.
    await EnsureUserAuthColumnsAsync(dbContext, dbOptions.Provider);

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
}

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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

// Adds any "Users" table columns introduced for Entra ID support that are missing on an
// already-existing database (EnsureCreated() does not retrofit schema changes). Safe to run
// on every startup: each statement is a no-op once the column/constraint change is applied.
static async Task EnsureUserAuthColumnsAsync(ApplicationDbContext dbContext, string provider)
{
    if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"AuthProvider\" text NOT NULL DEFAULT 'Local'");
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ALTER COLUMN \"PasswordHash\" DROP NOT NULL");
        return;
    }

    // SQLite has no "ADD COLUMN IF NOT EXISTS", so check pragma_table_info first. Relaxing an
    // existing NOT NULL constraint on SQLite requires a full table rebuild, which is not done
    // here — local SQLite databases are disposable dev stores, so delete the database file to
    // pick up the relaxed PasswordHash constraint via a fresh EnsureCreated().
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'AuthProvider'";
    var hasAuthProviderColumn = Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;

    if (!hasAuthProviderColumn)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Users\" ADD COLUMN \"AuthProvider\" TEXT NOT NULL DEFAULT 'Local'");
    }
}

