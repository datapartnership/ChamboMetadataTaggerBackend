# Configuration Guide

This project uses the **Options Pattern** for type-safe configuration management.

## Configuration Classes

### 1. AzureAd (Microsoft.Identity.Web)

Provided by `Microsoft.Identity.Web`. Binds to the `AzureAd` section automatically when `AddMicrosoftIdentityWebApiAuthentication` is called.

| Property | Description |
|---|---|
| `Instance` | Azure AD endpoint — use `https://login.microsoftonline.com/` |
| `TenantId` | Your Azure AD tenant ID (GUID) |
| `ClientId` | The app registration Client ID of this API |
| `Audience` | The audience expected in incoming tokens — typically `api://<ClientId>` |

### 2. AzureAdGroupMappingOptions

Defined in `Options/AzureAdGroupMappingOptions.cs`. Maps Azure AD Group object IDs to application roles.

```csharp
public class AzureAdGroupMappingOptions
{
    public const string Section = "AzureAdGroupMapping";

    public string AdminGroupId { get; set; } = string.Empty;
    public string TaggerGroupId { get; set; } = string.Empty;
    public string SupervisorGroupId { get; set; } = string.Empty;
}
```

### 3. StorageOptions / S3StorageOptions / AzureStorageOptions

Control the file storage backend (Azure Blob Storage or S3).

### 4. DatabaseOptions

Controls the database backend (PostgreSQL or SQLite).

### 5. CorsOptions

Configures allowed CORS origins.

---

## Registration in Program.cs

```csharp
// Azure AD authentication (reads AzureAd section automatically)
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);

// Group → role mapping
builder.Services.Configure<AzureAdGroupMappingOptions>(
    builder.Configuration.GetSection(AzureAdGroupMappingOptions.Section));
builder.Services.AddScoped<IClaimsTransformation, GroupRoleClaimsTransformation>();

// User auto-provisioning on first login
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
```

---

## Configuration File (appsettings.json)

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-api-app-registration-client-id>",
    "Audience": "api://<your-api-app-registration-client-id>"
  },
  "AzureAdGroupMapping": {
    "AdminGroupId": "<object-id-of-admin-group>",
    "TaggerGroupId": "<object-id-of-tagger-group>",
    "SupervisorGroupId": "<object-id-of-supervisor-group>"
  },
  "Database": {
    "Provider": "sqlite",
    "DataSource": "app.db"
  },
  "Storage": {
    "Provider": "AzureStorage",
    "AzureStorage": {
      "UseManagedIdentity": false,
      "AccountName": "",
      "ConnectionString": "<your-connection-string>",
      "ContainerName": "files"
    }
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
  }
}
```

---

## Environment Variables

ASP.NET Core maps environment variables using `__` as the section separator:

| Environment Variable | Description | Required |
|---|---|---|
| `AzureAd__TenantId` | Azure AD tenant ID | **Yes** |
| `AzureAd__ClientId` | API app registration client ID | **Yes** |
| `AzureAd__Audience` | Token audience (`api://<ClientId>`) | **Yes** |
| `AzureAdGroupMapping__AdminGroupId` | Azure AD group GUID for Admins | **Yes** |
| `AzureAdGroupMapping__TaggerGroupId` | Azure AD group GUID for Taggers | **Yes** |
| `AzureAdGroupMapping__SupervisorGroupId` | Azure AD group GUID for Supervisors | **Yes** |
| `Database__Provider` | `sqlite` or `postgresql` | No (default: `sqlite`) |
| `Database__DataSource` | SQLite file path | No (default: `app.db`) |
| `Storage__Provider` | `AzureStorage` or `s3` | **Yes** |
| `Storage__AzureStorage__ConnectionString` | Azure Blob Storage connection string | If using Azure Storage |
| `Storage__AzureStorage__ContainerName` | Blob container name | If using Azure Storage |
| `Cors__AllowedOrigins__0` | First allowed CORS origin | No |

> **Security note**: Never commit `appsettings.Development.json` containing real tenant IDs or secrets to source control.

---

## Azure AD App Registration Setup

### Step 1 — Register the API application

1. Go to **Azure Portal → Azure Active Directory → App registrations → New registration**
2. Name: e.g. `ChamboMetadataTagger API`
3. Set the **Application ID URI** (under *Expose an API*) to `api://<ClientId>`
4. Enable the **`groups`** claim:
   - Go to *Token configuration → Add groups claim*
   - Select **Security groups** → check **ID token** and **Access token**

### Step 2 — Create Azure AD groups

Create three security groups in Azure AD:
- `ChamboAdmins`
- `ChamboTaggers`
- `ChamboSupervisors`

Copy each group's **Object ID** and set it in `AzureAdGroupMapping`.

### Step 3 — Register the frontend (SPA) application

1. New app registration → Platform: **Single-page application**
2. Add the redirect URI for your frontend (e.g. `http://localhost:5173`)
3. Under *API permissions → Add a permission → My APIs* → select the API app → add the scope
4. Grant admin consent

The frontend uses this `clientId` + the API's `clientId` as the scope when calling `MSAL.js`.

---

## How Authentication Works

1. **Frontend** acquires an Azure AD access token via MSAL.js with scope `api://<ClientId>/.default`
2. **Frontend** sends the token as `Authorization: Bearer <token>` on every API request
3. **Backend** (`Microsoft.Identity.Web`) validates the token signature, issuer, audience, and expiry using Azure AD's OpenID Connect discovery document — no static secret key required
4. **`GroupRoleClaimsTransformation`** maps `groups` claim GUIDs → `ClaimTypes.Role` values (`Admin`, `Tagger`, `Supervisor`)
5. **`[Authorize(Roles = "Admin")]`** attributes on controllers enforce role-based access
6. **`UserProvisioningService`** finds or creates a local `User` DB row for the Azure identity on first request, preserving FK relationships with `FileAssignment` and `StudentSupervisor`

---

## ⚠️ Azure AD Groups Token Limit

If a user is a member of more than **200 groups**, Azure AD will not embed group IDs in the JWT. Instead the token will contain an overage indicator. In that scenario you must call Microsoft Graph to retrieve group memberships. For most deployments this limit is not reached, but if it is, `Microsoft.Identity.Web` supports this via `EnableTokenAcquisitionToCallDownstreamApi`.

---

## Benefits of the Options Pattern

1. **Type Safety**: Compile-time checking of configuration properties
2. **IntelliSense Support**: Auto-completion in IDEs
3. **Testability**: Easy to mock configuration in unit tests
4. **Isolation**: Each service gets only the configuration it needs

