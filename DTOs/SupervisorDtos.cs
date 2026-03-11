namespace MetadataTagging.DTOs;

public class AssignStudentToSupervisorRequest
{
    public int StudentId { get; set; }
    public int SupervisorId { get; set; }
}

public class StudentSupervisorDto
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public required string StudentUsername { get; set; }
    public required string StudentEmail { get; set; }
    public int SupervisorId { get; set; }
    public required string SupervisorUsername { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsActive { get; set; }
}

public class StudentWithStatsDto
{
    public int StudentId { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public int TotalAssigned { get; set; }
    public int TotalCompleted { get; set; }
    public int InProgress { get; set; }
    public List<FileMetadataDto> RecentFiles { get; set; } = new();
}

public class SupervisorReviewDto
{
    public int FileId { get; set; }
    public required string FileName { get; set; }
    public required string Status { get; set; }
    public int StudentId { get; set; }
    public required string StudentUsername { get; set; }
    public List<TagDto> Tags { get; set; } = new();
    public DateTime? CompletedAt { get; set; }
    public required string FileUrl { get; set; }
    public string? BlobName { get; set; }
    public bool IsCheckedBySupervisor { get; set; }
    public int? CheckedBySupervisorId { get; set; }
    public DateTime? CheckedAt { get; set; }
    public string? SupervisorNotes { get; set; }
}

public class MarkFileCheckedRequest
{
    public int FileId { get; set; }
    public int StudentId { get; set; }
    public string? Notes { get; set; }
}
