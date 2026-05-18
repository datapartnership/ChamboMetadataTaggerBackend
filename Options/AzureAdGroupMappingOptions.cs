namespace MetadataTagger.Options;

public class AzureAdGroupMappingOptions
{
    public const string Section = "AzureAdGroupMapping";

    public string AdminGroupId { get; set; } = string.Empty;
    public string TaggerGroupId { get; set; } = string.Empty;
    public string SupervisorGroupId { get; set; } = string.Empty;
}
