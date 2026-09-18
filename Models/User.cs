namespace MetadataTagging.Models;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    /// <summary>
    /// BCrypt hash for local email/password login. Null for users provisioned
    /// exclusively via Entra ID, who cannot use the local login flow.
    /// </summary>
    public string? PasswordHash { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// How this user's identity is managed: "Local" (email/password) or "Entra"
    /// (just-in-time provisioned from an Azure Entra ID login).
    /// </summary>
    public string AuthProvider { get; set; } = UserAuthProviders.Local;

    public ICollection<FileAssignment> FileAssignments { get; set; } = new List<FileAssignment>();
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Tagger = "Tagger";
    public const string Supervisor = "Supervisor";
}

public static class UserAuthProviders
{
    public const string Local = "Local";
    public const string Entra = "Entra";
}
