using MetadataTagging.Models;

namespace MetadataTagging.Services;

public interface IEntraUserProvisioningService
{
    /// <summary>
    /// Finds or just-in-time provisions a local <see cref="User"/> for a successful
    /// Entra ID login, keeping Username/Role/LastLoginAt in sync with the Entra claims.
    /// </summary>
    /// <param name="email">Email/UPN claim from the validated Entra ID token.</param>
    /// <param name="displayName">Display name claim from the token, if present.</param>
    /// <param name="entraRoles">The Entra App Roles assigned to the signed-in user.</param>
    /// <returns>
    /// The local user to authenticate as, or <c>null</c> if the login must be rejected
    /// (no recognized app role, or a matching local account is deactivated).
    /// </returns>
    Task<User?> ProvisionOrSyncUserAsync(string email, string? displayName, IEnumerable<string> entraRoles);
}
