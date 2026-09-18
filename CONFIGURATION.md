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

### 4. EntraIdOptions

Optional configuration (`Options/EntraIdOptions.cs`) for accepting Azure Entra ID
(Azure AD) access tokens **alongside** the local email/password JWT login. Entra ID
authentication is only enabled when both `TenantId` and `ClientId` are set.

```csharp
public class EntraIdOptions
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string RoleClaimType { get; set; } = "roles";
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
  "EntraId": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "",
    "ClientId": "",
    "Audience": "",
    "RoleClaimType": "roles"
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

## Azure Entra ID (Azure AD) Authentication (optional)

The API can accept **Entra ID-issued access tokens** directly as `Authorization: Bearer <token>`,
in addition to the local email/password JWT login. This is disabled by default and
only activates once `EntraId:TenantId` and `EntraId:ClientId` (or the equivalent
`EntraId__TenantId` / `EntraId__ClientId` environment variables) are set.

### How it works

1. Both a local JWT scheme (`Local`) and an Entra ID scheme (`EntraId`) are
   registered. A policy scheme inspects each incoming bearer token's issuer and
   routes it to the right scheme, so existing `[Authorize(Roles = ...)]`
   attributes on controllers require no changes.
2. Entra ID tokens must carry a `roles` claim (Entra **App Roles**) with one of
   `Admin`, `Supervisor`, or `Tagger` — tokens without a recognized role are rejected.
3. On first successful Entra ID login, a local `Users` row is automatically
   created (and kept in sync on subsequent logins) from the token's
   email/name/role claims, so existing per-user data (file assignments, audit
   fields, etc.) keeps working against the same integer `Users.Id`.

### Entra app registration setup

1. In the Azure Portal, register an application (or reuse an existing one) for this API.
2. Under **Expose an API**, add an Application ID URI (e.g. `api://<client-id>`) and
   at least one scope if the calling client needs one.
3. Under **App roles**, create three app roles with **Value** set exactly to
   `Admin`, `Supervisor`, and `Tagger` (allowed member types: Users/Groups or Applications, as needed).
4. In **Enterprise applications** → your app → **Users and groups**, assign each
   user to the appropriate app role.
5. Set the following configuration (via `appsettings.json` or environment variables):

| Setting | Description | Required |
|---|---|---|
| `EntraId__TenantId` | Azure Entra ID tenant ID (GUID) | **Yes** (to enable Entra ID auth) |
| `EntraId__ClientId` | Application (client) ID of the app registration | **Yes** (to enable Entra ID auth) |
| `EntraId__Instance` | Azure AD instance | No (default: `https://login.microsoftonline.com/`) |
| `EntraId__Audience` | Expected token audience, if different from `ClientId` | No |
| `EntraId__RoleClaimType` | Claim type carrying the App Roles | No (default: `roles`) |


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
