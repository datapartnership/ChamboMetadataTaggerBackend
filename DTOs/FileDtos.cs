namespace MetadataTagging.DTOs;

public class FileMetadataDto
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string FileUrl { get; set; }
    public string? BlobName { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public string FileCategory { get; set; } = "Other";
    public DateTime UploadedAt { get; set; }
    public required string Status { get; set; }
    public DateTime? TaggingCompletedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public List<TagDto> Tags { get; set; } = new();
    public List<int> AssignedToUserIds { get; set; } = new();
    public string? SupervisorNotes { get; set; }
    public DateTime? SupervisorCheckedAt { get; set; }
}

public class CreateFileMetadataRequest
{
    public required string FileName { get; set; }
    public required string FileUrl { get; set; }
    public string? BlobName { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
}

public class AssignFileRequest
{
    public int FileId { get; set; }
    public int UserId { get; set; }
}

public class AssignBlobFileRequest
{
    public required string BlobName { get; set; }
    public int UserId { get; set; }
}

public class AssignMultipleFilesRequest
{
    public List<string> BlobNames { get; set; } = new();
    public int UserId { get; set; }
}

public class AssignMultipleFilesResult
{
    public int TotalRequested { get; set; }
    public int SuccessfullyAssigned { get; set; }
    public int Failed { get; set; }
    public List<string> FailedBlobNames { get; set; } = new();
}

public class SyncBlobFilesResponse
{
    public int TotalBlobs { get; set; }
    public int ImportedFiles { get; set; }
    public int ExistingFiles { get; set; }
}

public class AddTagsRequest
{
    public List<TagDto> Tags { get; set; } = new();
}

public class TagDto
{
    public required string TagKey { get; set; }
    public required string TagValue { get; set; }
}

public class BlobFileDto
{
    public required string BlobName { get; set; }
    public required string FileUrl { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public string FileCategory { get; set; } = "Other";
    public DateTime? LastModified { get; set; }
}

public class FilePreviewDto
{
    public int FileId { get; set; }
    public required string FileName { get; set; }
    public required string BlobName { get; set; }
    public required string PreviewUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
    public string FileCategory { get; set; } = "Other";
    public double? DurationSeconds { get; set; }
}

public class UpdateAudioMetadataRequest
{
    public double DurationSeconds { get; set; }
}

public class TaggingProgressDto
{
    public int UserId { get; set; }
    public required string Username { get; set; }
    public int TotalAssigned { get; set; }
    public int TotalInProgress { get; set; }
    public int TotalSubmitted { get; set; }
    public int TotalSentBack { get; set; }
    public int TotalApproved { get; set; }
    public List<CompletedFileDto> CompletedFiles { get; set; } = new();
}

public class CompletedFileDto
{
    public int FileId { get; set; }
    public required string FileName { get; set; }
    public DateTime? CompletedAt { get; set; }
}
