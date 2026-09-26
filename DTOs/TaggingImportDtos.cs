namespace MetadataTagging.DTOs;

public class TaggingImportResult
{
    public int TaggerUserId { get; set; }
    public int ApproverUserId { get; set; }
    public int FilesImported { get; set; }
    public int AssignmentsInDump { get; set; }
    public int AssignmentsImported { get; set; }
    public int TagsImported { get; set; }
}
