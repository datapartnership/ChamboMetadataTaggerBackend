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
        UserRoles.Tagger,
        UserRoles.Consumer
    ];

    private const int UsernameMaxLength = 100;

    private readonly ApplicationDbContext _context;

    public EntraUserProvisioningService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ProvisionOrSyncUserAsync(
        string email,
        string? displayName,
        IEnumerable<string> roles,
        string authProvider = UserAuthProviders.Entra)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var role = RolePrecedence.FirstOrDefault(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
        if (role == null)
        {
            // No recognized Admin/Supervisor/Tagger/Consumer role assigned by the identity provider.
            return null;
        }

        var normalizedEmail = email.Trim();
        var user = await FindByEmailAsync(normalizedEmail);

        if (user != null)
        {
            return await SyncExistingUserAsync(user, displayName, role, authProvider);
        }

        user = new User
        {
            Username = await ResolveUniqueUsernameAsync(displayName, normalizedEmail, excludeUserId: null),
            Email = normalizedEmail,
            PasswordHash = null,
            Role = role,
            AuthProvider = authProvider,
            IsActive = true,
            LastLoginAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync();
            return user;
        }
        catch (DbUpdateException)
        {
            // Concurrent first-login requests can race to insert the same user.
            _context.Entry(user).State = EntityState.Detached;

            var existing = await FindByEmailAsync(normalizedEmail);
            if (existing == null)
            {
                throw;
            }

            return await SyncExistingUserAsync(existing, displayName, role, authProvider);
        }
    }

    private Task<User?> FindByEmailAsync(string email) =>
        _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

    private async Task<User?> SyncExistingUserAsync(User user, string? displayName, string role, string authProvider)
    {
        if (!user.IsActive)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(displayName) && user.Username != displayName.Trim())
        {
            user.Username = await ResolveUniqueUsernameAsync(displayName, user.Email, user.Id, fallback: user.Username);
        }

        user.Role = role;
        user.AuthProvider = authProvider;
        user.LastLoginAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<string> ResolveUniqueUsernameAsync(
        string? displayName,
        string email,
        int? excludeUserId,
        string? fallback = null)
    {
        var candidates = new[] { displayName?.Trim(), fallback, email }
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Truncate(c!, UsernameMaxLength))
            .Distinct();

        foreach (var candidate in candidates)
        {
            if (!await UsernameTakenAsync(candidate, excludeUserId))
            {
                return candidate;
            }
        }

        var baseName = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : email;
        for (var suffix = 2; ; suffix++)
        {
            var tail = $" ({suffix})";
            var candidate = Truncate(baseName, UsernameMaxLength - tail.Length) + tail;
            if (!await UsernameTakenAsync(candidate, excludeUserId))
            {
                return candidate;
            }
        }
    }

    private Task<bool> UsernameTakenAsync(string username, int? excludeUserId) =>
        _context.Users.AnyAsync(u => u.Username == username && (excludeUserId == null || u.Id != excludeUserId));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
