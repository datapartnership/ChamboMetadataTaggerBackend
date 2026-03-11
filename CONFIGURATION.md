# Configuration Guide

This project uses the **Options Pattern** for type-safe configuration management, which is the recommended approach in ASP.NET Core applications.

## Configuration Classes

All configuration classes are defined in `Models/AppSettings.cs`:

### 1. JwtSettings
```csharp
public class JwtSettings
{
    public required string SecretKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int ExpiryMinutes { get; set; } = 480;
}
```

### 2. AzureBlobStorageSettings
```csharp
public class AzureBlobStorageSettings
{
    public required string ConnectionString { get; set; }
    public required string ContainerName { get; set; }
}
```

### 3. DefaultAdminSettings
```csharp
public class DefaultAdminSettings
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
}
```

## Registration in Program.cs

Configuration options are registered in `Program.cs`:

```csharp
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<AzureBlobStorageSettings>(builder.Configuration.GetSection("AzureBlobStorage"));
builder.Services.Configure<DefaultAdminSettings>(builder.Configuration.GetSection("DefaultAdmin"));
```

## Usage in Services

### Example: AuthService

```csharp
public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtSettings _jwtSettings;

    public AuthService(ApplicationDbContext context, IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
    }

    private string GenerateJwtToken(User user)
    {
        // Use _jwtSettings directly
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        // ...
    }
}
```

### Example: AzureBlobService

```csharp
public class AzureBlobService : IAzureBlobService
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobService(IOptions<AzureBlobStorageSettings> blobSettings)
    {
        var settings = blobSettings.Value;
        var blobServiceClient = new BlobServiceClient(settings.ConnectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(settings.ContainerName);
    }
}
```

## Configuration File (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=metadatatagging.db"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-here-min-32-characters-long-change-in-production",
    "Issuer": "MetadataTaggingAPI",
    "Audience": "MetadataTaggingClient",
    "ExpiryMinutes": 480
  },
  "AzureBlobStorage": {
    "ConnectionString": "your-azure-storage-connection-string",
    "ContainerName": "files"
  },
  "DefaultAdmin": {
    "Username": "admin",
    "Email": "admin@metadatatagging.com",
    "Password": "Admin123!"
  }
}
```

## Benefits of the Options Pattern

1. **Type Safety**: Compile-time checking of configuration properties
2. **IntelliSense Support**: Auto-completion in IDEs
3. **Validation**: Can add data annotations for configuration validation
4. **Testability**: Easy to mock configuration in unit tests
5. **Change Tracking**: Can use `IOptionsMonitor<T>` to react to configuration changes
6. **Isolation**: Each service gets only the configuration it needs

## Environment-Specific Configuration

You can override settings using environment-specific files:

- `appsettings.Development.json` - Development environment
- `appsettings.Production.json` - Production environment
- Environment variables - Highest priority

Example environment variable:
```bash
export JwtSettings__SecretKey="your-production-secret-key"
export DefaultAdmin__Password="ProductionPassword123!"
```

Note: Double underscore `__` is used as the section delimiter in environment variables.

## Best Practices

1. **Required Properties**: Use `required` keyword for mandatory configuration
2. **Default Values**: Provide sensible defaults where appropriate
3. **Validation**: Consider using `IValidateOptions<T>` for complex validation
4. **Sensitive Data**: Never commit production secrets to source control
5. **Documentation**: Document all configuration options and their purpose
