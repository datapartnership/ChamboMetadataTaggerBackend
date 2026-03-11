namespace MetadataTagging.Models;

public class FileMetadata
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string FileUrl { get; set; }
    public string? BlobName { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public FileTaggingStatus Status { get; set; } = FileTaggingStatus.Unassigned;
    public DateTime? TaggingCompletedAt { get; set; }
    public double? DurationSeconds { get; set; }

    public ICollection<FileAssignment> FileAssignments { get; set; } = new List<FileAssignment>();
    public ICollection<FileTag> Tags { get; set; } = new List<FileTag>();
}

public enum FileTaggingStatus
{
    Unassigned,
    Assigned,
    InProgress,
    Completed
}
