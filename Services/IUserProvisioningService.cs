using System.Security.Claims;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public interface IUserProvisioningService
{
    /// <summary>
    /// Finds the local User record matching the Azure AD principal,
    /// creating or linking one on first login. Returns null if the
    /// principal carries no Azure Object ID claim.
    /// </summary>
    Task<User?> GetOrCreateAsync(ClaimsPrincipal principal);
}
