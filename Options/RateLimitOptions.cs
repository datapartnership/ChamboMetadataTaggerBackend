namespace MetadataTagger.Options;

/// <summary>Ported from the LRLL merge.</summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// Maximum number of file downloads allowed per user per calendar day (UTC).
    /// Set to 0 to disable the limit (unlimited downloads).
    /// </summary>
    public int DailyFileDownloadLimit { get; set; } = 0;
}
