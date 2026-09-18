using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MetadataTagging.Services;

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
    public static async Task NormalizeAsync(TokenValidatedContext context, string roleClaimType)
    {
        var principal = context.Principal;
        if (principal == null)
        {
            context.Fail("Entra ID token produced no claims principal.");
            return;
        }

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(email))
        {
            context.Fail("Entra ID token did not contain an email/UPN claim.");
            return;
        }

        var displayName = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var entraRoles = principal.FindAll(roleClaimType).Select(c => c.Value).ToArray();

        var provisioningService = context.HttpContext.RequestServices
            .GetRequiredService<IEntraUserProvisioningService>();
        var localUser = await provisioningService.ProvisionOrSyncUserAsync(email, displayName, entraRoles);

        if (localUser == null)
        {
            context.Fail("No matching Admin/Supervisor/Tagger app role assigned in Entra ID, or the account is deactivated.");
            return;
        }

        var identity = new ClaimsIdentity(context.Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, localUser.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, localUser.Username));
        identity.AddClaim(new Claim(ClaimTypes.Email, localUser.Email));
        identity.AddClaim(new Claim(ClaimTypes.Role, localUser.Role));
        principal.AddIdentity(identity);
    }
}
