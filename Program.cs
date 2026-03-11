using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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
builder.Services.Configure<DefaultAdminSettings>(builder.Configuration.GetSection(DefaultAdminSettings.Section));
builder.Services.Configure<DefaultTaggerSettings>(builder.Configuration.GetSection(DefaultTaggerSettings.Section));
builder.Services.Configure<DefaultSupervisorSettings>(builder.Configuration.GetSection(DefaultSupervisorSettings.Section));

var dbOptions = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()
    ?? new DatabaseOptions();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbOptions.Provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql($"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Name};Username={dbOptions.Username};Password={dbOptions.Password}");
    else
        options.UseSqlite($"Data Source={dbOptions.DataSource}");
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();

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
