namespace MetadataTagging.Models;

public class FileAssignment
{
    public int Id { get; set; }
    public int FileMetadataId { get; set; }
    public int UserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCompleted { get; set; } = false;
    public bool IsCheckedBySupervisor { get; set; } = false;
    public int? CheckedBySupervisorId { get; set; }
    public DateTime? CheckedAt { get; set; }
    public string? SupervisorNotes { get; set; }

    public FileMetadata FileMetadata { get; set; } = null!;
    public User User { get; set; } = null!;
}
