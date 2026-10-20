# Configuration Guide

The API uses the **Options pattern**: each configuration section is bound to a typed class and injected with `IOptions<T>`. Nothing reads `Configuration["..."]` directly.

## Sources and precedence

Later sources override earlier ones:

1. `appsettings.json` (committed; only safe defaults and empty placeholders)
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. Environment variables (including a `.env` file loaded by Docker Compose or `load_env.sh`)

Environment variables use `__` (double underscore) as the section separator and as the array index separator:

```bash
JwtSettings__SecretKey=...
Cors__AllowedOrigins__0=http://localhost:5173   # array item 0
Cors__AllowedOrigins__1=http://localhost:5000   # array item 1
```

> A single underscore (`Cors__AllowedOrigins_0`) is **not** recognised and the value is silently ignored.

Copy `.env.example` to `.env` and fill it in. `.env` is gitignored. Never commit real secrets.

## Options classes

All live in `Options/` except the default-user settings in `Models/AppSettings.cs`.

| Section | Class | Purpose |
|---|---|---|
| `Authentication` | `AuthenticationOptions` | Selects the active auth method |
| `JwtSettings` | `JwtOptions` | Local email/password JWT |
| `EntraId` | `EntraIdOptions` | Azure Entra ID tokens |
| `Keycloak` | `KeycloakOptions` | Keycloak tokens |
| `Database` | `DatabaseOptions` | PostgreSQL connection |
| `CatalogStorage` | `CatalogStorageOptions` | The only file storage (local / S3 / Azure) |
| `RateLimit` | `RateLimitOptions` | Daily catalog download cap |
| `Cors` | `CorsOptions` | Allowed browser origins |
| `DefaultAdmin`, `DefaultTagger`, `DefaultSupervisor` | `Default*Settings` | Users seeded at startup |

## Authentication

**Exactly one** method is active, chosen by `Authentication:Method` (`Jwt` default, `EntraId` or `Keycloak`; case-insensitive). Only the selected provider is validated and registered. The other sections may stay populated for other environments and are ignored. An unknown value stops startup.

| Setting | Default | Notes |
|---|---|---|
| `Authentication__Method` | `Jwt` | `Jwt`, `EntraId` or `Keycloak` |

`POST /api/auth/login` (email/password) only works when the method is `Jwt`. For the other methods the API validates bearer tokens issued by the identity provider.

### Jwt

| Setting | Required | Notes |
|---|---|---|
| `JwtSettings__SecretKey` | **Yes** | Signing key, at least 32 characters. Generate with `openssl rand -base64 48` |
| `JwtSettings__Issuer` | **Yes** | No default |
| `JwtSettings__Audience` | **Yes** | No default |
| `JwtSettings__ExpiryMinutes` | No | Default `480` |

`appsettings.json` has no `JwtSettings` section, so with the default method the app **will not start** until these come from the environment. Missing `SecretKey` throws at startup.

### EntraId

Requires `EntraId:TenantId` and `EntraId:ClientId`; otherwise startup fails.

| Setting | Required | Default |
|---|---|---|
| `EntraId__TenantId` | **Yes** | |
| `EntraId__ClientId` | **Yes** | |
| `EntraId__Instance` | No | `https://login.microsoftonline.com/` |
| `EntraId__Audience` | No | Expected token audience; also the base of the Swagger `user_impersonation` scope. Set it to your Application ID URI |
| `EntraId__RoleClaimType` | No | `roles` (Entra App Roles) |
| `EntraId__GroupClaimType` | No | `groups` |
| `EntraId__GroupRoleMappings__N__GroupId` / `__Role` | No | Maps a group object ID (GUID) to `Admin`, `Supervisor` or `Tagger` |

Roles come from a `roles` claim (App Roles whose value is exactly `Admin`, `Supervisor` or `Tagger`) or from a `groups` claim matched against `GroupRoleMappings`. Several groups may map to one role; if a user matches several roles the precedence is Admin, then Supervisor, then Tagger. Group *overage* claims (needing a Microsoft Graph lookup) are not supported. Invalid mappings (non-GUID group ID or unknown role) stop startup.

```json
"EntraId": {
  "GroupRoleMappings": [
    { "GroupId": "<admin-group-object-id>", "Role": "Admin" },
    { "GroupId": "<tagger-group-object-id>", "Role": "Tagger" }
  ]
}
```

On first successful sign-in a local `Users` row is created (and kept in sync afterwards) from the token's email, name and role claims, so per-user data keeps pointing at a stable integer `Users.Id`. The frontend calls `GET /api/Auth/me` with its Entra access token to get the provisioned user.

App registration checklist: expose an API (Application ID URI), create the three App Roles, and assign users or groups to them in Enterprise applications.

### Keycloak

Requires `Keycloak:Authority`; otherwise startup fails.

| Setting | Required | Default |
|---|---|---|
| `Keycloak__Authority` | **Yes** | Realm URL, e.g. `https://kc.example.org/realms/myrealm` |
| `Keycloak__ClientId` | **Yes** (for role mapping and Swagger) | Roles are read from `resource_access[ClientId].roles` |
| `Keycloak__Audience` | No | |
| `Keycloak__RequireHttpsMetadata` | No | `true` (set `false` only for local HTTP Keycloak) |
| `Keycloak__RoleMappings__N__KeycloakRole` / `__Role` | No | Maps a client role to `Admin`, `Supervisor` or `Tagger` |

Every `RoleMappings` entry must have a non-blank `KeycloakRole` and a valid `Role`. **Do not add blank entries**, or startup throws. Leave the whole array out when unused. A local `Users` row is created on first login with `AuthProvider = "Keycloak"`.

## Database

| Setting | Default | Notes |
|---|---|---|
| `Database__Host` | `localhost` | Docker Compose overrides this to `db` |
| `Database__Port` | `5432` | Integer |
| `Database__Name` | | |
| `Database__Username` | | |
| `Database__Password` | | Unused with managed identity |
| `Database__UseManagedIdentity` | `false` | Azure Database for PostgreSQL token auth |
| `Database__ManagedIdentityClientId` | | User-assigned identity client ID |
| `Database__ResetOnStartup` | `false` | **Drops every table on each start.** Use once for a migration, then set back to `false` |

The schema is applied by EF Core migrations at startup.

## CatalogStorage

The application's single file store. Content is laid out as `{Provider}/{Collection}/...`. Taggers read from it, `POST /api/seed` scans it, and catalog downloads are served from it.

| Setting | Default | Notes |
|---|---|---|
| `CatalogStorage__Provider` | `local` | `local`, `s3` or `azure` |
| `CatalogStorage__FileStorageRoot` | | `local` only: root folder |
| `CatalogStorage__S3__ServiceUrl` / `BucketName` | | Required for `s3` (empty values throw) |
| `CatalogStorage__S3__AccessKey` / `SecretKey` | | |
| `CatalogStorage__S3__ForcePathStyle` | `false` | Needed by many S3-compatible stores |
| `CatalogStorage__S3__Region` | `us-east-1` | |
| `CatalogStorage__Azure__ConnectionString` | | Key-based auth. If empty, `ServiceUri` plus `DefaultAzureCredential` is used |
| `CatalogStorage__Azure__ServiceUri` | | e.g. `https://<account>.blob.core.windows.net/` |
| `CatalogStorage__Azure__ContainerName` | | |
| `CatalogStorage__Azure__Prefix` | | Blob key prefix in front of `Provider/Collection`; empty = container root |
| `CatalogStorage__Azure__ManagedIdentityClientId` | | Empty = system-assigned identity or developer credentials |

Preview URLs need `s3` (presigned) or `azure` (SAS; a user-delegation key is used with `ServiceUri` and a managed identity). With `local`, preview endpoints return 400 and only `/stream` endpoints work.

## Other settings

| Setting | Default | Notes |
|---|---|---|
| `RateLimit__DailyFileDownloadLimit` | `0` | Per-user daily cap on catalog downloads; `0` = unlimited. `appsettings.Development.json` sets `10` |
| `Cors__AllowedOrigins__N` | `[]` | Empty list means **all cross-origin requests are denied** |
| `DefaultAdmin__*`, `DefaultTagger__*`, `DefaultSupervisor__*` | not set | `Username`, `Email`, `Password`. A user is created at startup only if all three are set and no user with that email exists. No defaults ship with the code |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` enables Swagger UI and `appsettings.Development.json`. The Docker images do not set it |
| `Logging`, `AllowedHosts` | | Standard ASP.NET Core |

## Docker

- `docker-compose.yml` loads `.env` into the `api` service and overrides `Database__Host=db`.
- The `db` container takes its database name, user and password from `Database__Name`, `Database__Username` and `Database__Password`, so set those in `.env` before `docker compose up` (compose refuses to start otherwise). Postgres is only published on `127.0.0.1:5432`.
- The default `Dockerfile` listens on port **80** (compose maps `8080:80`); `Dockerfile.dev` listens on 8080. `Dockerfile.wbg` is the hardened chiseled image, also on port 80.
- Compose also runs **Keycloak** (`localhost:8081`, admin/admin, data stored in the `keycloak` Postgres database, realm `chambo` imported from `keycloak/chambo-realm.json` on first start only; to re-import, drop that database) and **MertBlobStorage** (`localhost:8443`, image `mertblobstorage:latest`, built from `~/Projects/MertBlobStorage`). The `api` service is forced to `Authentication__Method=Keycloak` and `CatalogStorage__Provider=azure`, with the connection string built from `MertBlob__AccountName` / `MertBlob__AccountKey` (generate the key with `openssl rand -base64 32`). A one-shot `mertblob-init` service creates the catalog container.
- Sample Keycloak users: `admin@chambo.local` / `admin` (Admin) and `tagger@chambo.local` / `tagger` (Tagger). Frontend client: `chambo-frontend` (public, PKCE).
- `load_env.sh` exports a `.env` into your shell: `source load_env.sh .env` (blank values are skipped).

## Secrets hygiene

- Keep real values in `.env` or your secret store, never in `appsettings*.json` or `.env.example`.
- `nuget.config` (private feed credential) must not be committed; use an environment variable or CI secret for the feed password.
- Tenant, client and group IDs are identifiers rather than secrets, but belong in per-environment config, not in shared defaults.
