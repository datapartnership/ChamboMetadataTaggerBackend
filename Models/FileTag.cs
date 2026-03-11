namespace MetadataTagging.Models;

public class FileTag
{
    public int Id { get; set; }
    public int FileMetadataId { get; set; }
    public required string TagKey { get; set; }
    public required string TagValue { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public FileMetadata FileMetadata { get; set; } = null!;
}
