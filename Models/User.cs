namespace MetadataTagging.Models;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<FileAssignment> FileAssignments { get; set; } = new List<FileAssignment>();
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Tagger = "Tagger";
    public const string Supervisor = "Supervisor";
}
