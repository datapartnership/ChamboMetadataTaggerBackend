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
2. Authorization accepts either a `roles` claim (Entra **App Roles**) with one of
   `Admin`, `Supervisor`, or `Tagger`, or a `groups` claim containing an object ID
   configured in `EntraId:GroupRoleMappings`.
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
| `EntraId__GroupClaimType` | Claim type carrying Entra group object IDs | No (default: `groups`) |
| `EntraId__GroupRoleMappings` | Array mapping group object IDs to `Admin`, `Supervisor`, or `Tagger` | No |

Example group mappings:

```json
{
  "EntraId": {
    "GroupClaimType": "groups",
    "GroupRoleMappings": [
      { "GroupId": "<admin-group-object-id>", "Role": "Admin" },
      { "GroupId": "<supervisor-group-object-id>", "Role": "Supervisor" },
      { "GroupId": "<tagger-group-object-id>", "Role": "Tagger" }
    ]
  }
}
```

Multiple group IDs may map to the same role. If a user belongs to groups mapped
to different roles, the existing precedence is `Admin`, then `Supervisor`, then
`Tagger`. Tokens using Entra group overage claims instead of an inline `groups`
claim require Microsoft Graph resolution and are not supported by this mapping.

The frontend sends its Entra access token to `GET /api/Auth/me` after the configured
`/callback` route completes. This validates the token and returns the provisioned local
application user for the role-specific frontend dashboard.

## Keycloak Authentication (optional)

The API can also accept **Keycloak-issued access tokens**, as a third scheme alongside
the local email/password JWT login and Entra ID. This is disabled by default and only
activates once `Keycloak:Authority` (or `Keycloak__Authority`) is set.

### How it works

1. A third JWT bearer scheme (`Keycloak`) is registered alongside `Local` and `EntraId`.
   The same policy scheme that routes requests by issuer for Entra ID also recognizes
   Keycloak-issued tokens (by comparing the token issuer to `Keycloak:Authority`), so
   existing `[Authorize(Roles = ...)]`/policy attributes require no changes.
2. Keycloak client roles are read from the token's `resource_access[ClientId].roles`
   claim and mapped onto `Admin`, `Supervisor`, or `Tagger` via `Keycloak:RoleMappings`
   — the same pattern `EntraId:GroupRoleMappings` uses for Entra ID groups.
3. On first successful Keycloak login, a local `Users` row is automatically created
   (and kept in sync on subsequent logins), with `AuthProvider` set to `"Keycloak"`.

| Setting | Description | Required |
|---|---|---|
| `Keycloak__Authority` | Keycloak realm base URL (e.g. `https://kc.example.org/realms/myrealm`) | **Yes** (to enable Keycloak auth) |
| `Keycloak__Audience` | Expected token audience | No |
| `Keycloak__ClientId` | Keycloak client ID (used to read `resource_access[ClientId].roles` and for Swagger UI's OAuth client) | **Yes** (to enable Keycloak auth) |
| `Keycloak__RequireHttpsMetadata` | Require HTTPS for the Keycloak metadata endpoint | No (default: `true`) |
| `Keycloak__RoleMappings` | Array mapping Keycloak client role names to `Admin`, `Supervisor`, or `Tagger` | No |

Example role mappings:

```json
{
  "Keycloak": {
    "Authority": "https://kc.example.org/realms/myrealm",
    "ClientId": "chambo-api",
    "RoleMappings": [
      { "KeycloakRole": "chambo-admin", "Role": "Admin" },
      { "KeycloakRole": "chambo-supervisor", "Role": "Supervisor" },
      { "KeycloakRole": "chambo-tagger", "Role": "Tagger" }
    ]
  }
}
```

## Catalog API configuration (merged from the LRLL API)

These sections configure the public catalog API endpoints (see the README's
"Catalog API" section for the endpoint list):

| Section | Purpose |
|---|---|
| `CatalogStorage` | The application's only file storage (`local`, `s3`, or `azure`), holding all content as `{Provider}/{Collection}/...` (audio/video collections contain a `*_metadata.jsonld`; text collections don't). Taggers and supervisors list, preview and stream files from it, `POST /api/seed` scans it to build the catalog, and catalog downloads are served from it. Preview URLs need `s3` (presigned) or `azure` (SAS: account key from the connection string, or a user delegation key when using `ServiceUri` + managed identity). With `local`, preview endpoints return 400 and only the `/stream` endpoints work. |
| `RateLimit` | `DailyFileDownloadLimit` — per-user daily cap on catalog file downloads (`0` = unlimited). |

See `.env.example` for the full list of environment-variable keys for each section.

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
