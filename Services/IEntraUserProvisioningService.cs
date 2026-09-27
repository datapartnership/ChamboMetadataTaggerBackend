using MetadataTagging.Models;

namespace MetadataTagging.Services;

public interface IEntraUserProvisioningService
{
    /// <summary>
    /// Finds or just-in-time provisions a local <see cref="User"/> for a successful
    /// federated login (Entra ID or Keycloak), keeping Username/Role/LastLoginAt in sync
    /// with the claims from the identity provider.
    /// </summary>
    /// <param name="email">Email/UPN claim from the validated token.</param>
    /// <param name="displayName">Display name claim from the token, if present.</param>
    /// <param name="roles">The application roles resolved from the identity provider's claims.</param>
    /// <param name="authProvider">
    /// Which <see cref="UserAuthProviders"/> value to record on the local user row.
    /// Defaults to <see cref="UserAuthProviders.Entra"/> for backward compatibility.
    /// </param>
    /// <returns>
    /// The local user to authenticate as, or <c>null</c> if the login must be rejected
    /// (no recognized role, or a matching local account is deactivated).
    /// </returns>
    Task<User?> ProvisionOrSyncUserAsync(
        string email,
        string? displayName,
        IEnumerable<string> roles,
        string authProvider = UserAuthProviders.Entra);
}
