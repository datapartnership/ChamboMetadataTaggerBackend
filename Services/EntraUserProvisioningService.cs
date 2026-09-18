using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class EntraUserProvisioningService : IEntraUserProvisioningService
{
    // Precedence used when a user is (unexpectedly) assigned more than one recognized
    // app role in Entra ID: the most privileged recognized role wins.
    private static readonly string[] RolePrecedence =
    [
        UserRoles.Admin,
        UserRoles.Supervisor,
        UserRoles.Tagger
    ];

    private readonly ApplicationDbContext _context;

    public EntraUserProvisioningService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ProvisionOrSyncUserAsync(string email, string? displayName, IEnumerable<string> entraRoles)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var role = RolePrecedence.FirstOrDefault(r => entraRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        if (role == null)
        {
            // No recognized Admin/Supervisor/Tagger app role assigned in Entra ID.
            return null;
        }

        var normalizedEmail = email.Trim();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail.ToLower());

        if (user != null)
        {
            if (!user.IsActive)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                user.Username = displayName;
            }

            user.Role = role;
            user.AuthProvider = UserAuthProviders.Entra;
            user.LastLoginAt = DateTime.UtcNow;
        }
        else
        {
            user = new User
            {
                Username = !string.IsNullOrWhiteSpace(displayName) ? displayName : normalizedEmail,
                Email = normalizedEmail,
                PasswordHash = null,
                Role = role,
                AuthProvider = UserAuthProviders.Entra,
                IsActive = true,
                LastLoginAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
        }

        await _context.SaveChangesAsync();
        return user;
    }
}
