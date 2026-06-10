using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class UserProvisioningService : IUserProvisioningService
{
    private readonly ApplicationDbContext _context;

    public UserProvisioningService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetOrCreateAsync(ClaimsPrincipal principal)
    {
        var oid = principal.FindFirst("oid")?.Value
               ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (string.IsNullOrEmpty(oid))
            return null;

        // Try to find by Azure Object ID (fast path for returning users)
        var user = await _context.Users.FirstOrDefaultAsync(u => u.AzureObjectId == oid);
        if (user != null)
        {
            // Sync role from current group membership and record login time
            var currentRole = ResolveRole(principal);
            if (!string.IsNullOrEmpty(currentRole) && user.Role != currentRole)
                user.Role = currentRole;
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return user;
        }

        var email = principal.FindFirst("preferred_username")?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? principal.FindFirst("upn")?.Value;

        // Try to link a pre-created record (admin pre-created by email, no OID yet)
        if (!string.IsNullOrEmpty(email))
        {
            user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
            if (user != null)
            {
                user.AzureObjectId = oid;
                var roleFromClaims = ResolveRole(principal);
                if (!string.IsNullOrEmpty(roleFromClaims))
                    user.Role = roleFromClaims;
                await _context.SaveChangesAsync();
                return user;
            }
        }

        // Auto-provision a brand-new user
        var displayName = principal.FindFirst("name")?.Value
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? email ?? oid;

        var newUser = new User
        {
            AzureObjectId = oid,
            Username = displayName,
            Email = email ?? string.Empty,
            Role = ResolveRole(principal) ?? UserRoles.Tagger,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();
        return newUser;
    }

    /// <summary>
    /// Returns the highest-priority role present in the principal's role claims,
    /// or null if no recognised role is found.
    /// </summary>
    private static string? ResolveRole(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();
        if (roles.Contains(UserRoles.Admin)) return UserRoles.Admin;
        if (roles.Contains(UserRoles.Supervisor)) return UserRoles.Supervisor;
        if (roles.Contains(UserRoles.Tagger)) return UserRoles.Tagger;
        return null;
    }
}
