using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MetadataTagging.Services;
using MetadataTagger.Options;

namespace MetadataTagging.Authentication;

/// <summary>
/// Normalizes a validated Keycloak token's claims into the same shape produced by the
/// local JWT login flow and Entra ID (see <see cref="EntraTokenClaimsNormalizer"/>), after
/// JIT-provisioning/syncing the corresponding local user record. This lets all existing
/// controllers keep reading <see cref="ClaimTypes.NameIdentifier"/> (the local integer user
/// id) and <see cref="ClaimTypes.Role"/> without any changes, regardless of which identity
/// provider issued the token.
/// </summary>
public static class KeycloakTokenClaimsNormalizer
{
    private const string ResourceAccessClaimType = "resource_access";

    public static async Task NormalizeAsync(TokenValidatedContext context, KeycloakOptions options)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(KeycloakTokenClaimsNormalizer));
        var principal = context.Principal;
        if (principal == null)
        {
            logger.LogWarning("Rejecting validated Keycloak token because it produced no claims principal.");
            context.Fail("Keycloak token produced no claims principal.");
            return;
        }

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Rejecting validated Keycloak token because it contains no supported email/preferred_username claim.");
            context.Fail("Keycloak token did not contain an email/preferred_username claim.");
            return;
        }

        var displayName = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var keycloakClientRoles = ResolveKeycloakClientRoles(principal, options.ClientId);
        var appRoles = options.RoleMappings
            .Where(mapping => keycloakClientRoles.Contains(mapping.KeycloakRole))
            .Select(mapping => mapping.Role);

        var provisioningService = context.HttpContext.RequestServices
            .GetRequiredService<IEntraUserProvisioningService>();
        var localUser = await provisioningService.ProvisionOrSyncUserAsync(
            email, displayName, appRoles, authProvider: Models.UserAuthProviders.Keycloak);

        if (localUser == null)
        {
            logger.LogWarning(
                "Rejecting validated Keycloak token because no active local user could be provisioned. " +
                "Confirm the token has a client role mapped via Keycloak:RoleMappings.");
            context.Fail("No matching Keycloak role mapping was found, or the account is deactivated.");
            return;
        }

        EntraTokenClaimsNormalizer.AddLocalApplicationIdentity(
            principal, localUser.Id, localUser.Username, localUser.Email, localUser.Role, context.Scheme.Name);
    }

    /// <summary>
    /// Flattens Keycloak client roles from the nested JWT claim
    /// <c>resource_access[clientId].roles</c>.
    /// </summary>
    internal static HashSet<string> ResolveKeycloakClientRoles(ClaimsPrincipal principal, string clientId)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(clientId))
            return roles;

        var resourceAccessClaim = principal.FindFirst(ResourceAccessClaimType);
        if (resourceAccessClaim is null)
            return roles;

        try
        {
            using var doc = JsonDocument.Parse(resourceAccessClaim.Value);
            if (doc.RootElement.TryGetProperty(clientId, out var clientAccess) &&
                clientAccess.TryGetProperty("roles", out var rolesArray) &&
                rolesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in rolesArray.EnumerateArray())
                {
                    var roleName = role.GetString();
                    if (!string.IsNullOrEmpty(roleName))
                        roles.Add(roleName);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed claim — treat as no roles.
        }

        return roles;
    }
}
