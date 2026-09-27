using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MetadataTagging.Services;
using MetadataTagger.Options;

namespace MetadataTagging.Authentication;

/// <summary>
/// Normalizes a validated Entra ID token's claims into the same shape produced by
/// the local JWT login flow (see <see cref="MetadataTagging.Services.AuthService"/>),
/// after JIT-provisioning/syncing the corresponding local user record. This lets all
/// existing controllers keep reading <see cref="ClaimTypes.NameIdentifier"/> (the
/// local integer user id) and <see cref="ClaimTypes.Role"/> without any changes.
/// </summary>
public static class EntraTokenClaimsNormalizer
{
    public static async Task NormalizeAsync(TokenValidatedContext context, EntraIdOptions options)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EntraTokenClaimsNormalizer));
        var principal = context.Principal;
        if (principal == null)
        {
            logger.LogWarning("Rejecting validated Entra token because it produced no claims principal.");
            context.Fail("Entra ID token produced no claims principal.");
            return;
        }

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Rejecting validated Entra token because it contains no supported email or UPN claim.");
            context.Fail("Entra ID token did not contain an email/UPN claim.");
            return;
        }

        var displayName = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var entraRoles = EntraGroupRoleResolver.ResolveRoles(principal, options);

        var provisioningService = context.HttpContext.RequestServices
            .GetRequiredService<IEntraUserProvisioningService>();
        var localUser = await provisioningService.ProvisionOrSyncUserAsync(email, displayName, entraRoles);

        if (localUser == null)
        {
            logger.LogWarning(
                "Rejecting validated Entra token because no active local user could be provisioned. " +
                "Confirm the token has an assigned {RoleClaimType} app role or a configured {GroupClaimType} group mapping.",
                options.RoleClaimType,
                options.GroupClaimType);
            context.Fail("No matching application role or group mapping was found, or the account is deactivated.");
            return;
        }

        AddLocalApplicationIdentity(principal, localUser.Id, localUser.Username, localUser.Email, localUser.Role, context.Scheme.Name);
    }

    internal static void AddLocalApplicationIdentity(
        ClaimsPrincipal principal,
        int userId,
        string username,
        string email,
        string role,
        string authenticationType)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToArray())
            {
                identity.TryRemoveClaim(claim);
            }
        }

        var applicationIdentity = new ClaimsIdentity(authenticationType);
        applicationIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        applicationIdentity.AddClaim(new Claim(ClaimTypes.Name, username));
        applicationIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
        applicationIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
        principal.AddIdentity(applicationIdentity);
    }
}
