using MetadataTagging.Models;

namespace MetadataTagging.Services;

public interface IAuthService
{
    Task<string?> AuthenticateAsync(string email, string password);
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserByEmailAsync(string email);
    int? GetUserIdFromToken(string token);
    string? GetUserRoleFromToken(string token);
}
