namespace MetadataTagging.DTOs;

// ── Croissant JSON-LD response (query success) ─────────────────────────────

/// <summary>Croissant 1.0 JSON-LD document returned by the query endpoint.</summary>
public class CroissantResponse
{
    /// <summary>JSON-LD context array</summary>
    /// <example>["https://mlcommons.org/working-groups/data/croissant/"]</example>
    public object[] @context { get; set; } = [];

    /// <summary>UTC timestamp when the response was generated</summary>
    /// <example>2026-03-02T12:00:00.0000000Z</example>
    public string generatedAt { get; set; } = "";

    /// <summary>Information about sampling applied to the result set</summary>
    public SamplingInfo samplingInfo { get; set; } = new();

    /// <summary>Array of sc:Dataset nodes, one per year</summary>
    public DatasetNode[] @graph { get; set; } = [];
}

/// <summary>Describes how the result set was sampled.</summary>
public class SamplingInfo
{
    /// <summary>Total files matching the filters</summary>
    /// <example>142</example>
    public int totalMatched { get; set; }

    /// <summary>Requested limit</summary>
    /// <example>30</example>
    public int limit { get; set; }

    /// <summary>Number of files actually returned</summary>
    /// <example>30</example>
    public int returned { get; set; }

    /// <summary>True when returned &lt; totalMatched (random sample was taken)</summary>
    /// <example>true</example>
    public bool randomSample { get; set; }
}

/// <summary>A sc:Dataset node representing one year of the corpus.</summary>
public class DatasetNode
{
    /// <example>sc:Dataset</example>
    public string @type { get; set; } = "sc:Dataset";

    /// <example>nation-2024</example>
    public string @id { get; set; } = "";

    /// <example>Nation Newspaper 2024</example>
    public string? scName { get; set; }

    /// <example>2024</example>
    public string? scTemporalCoverage { get; set; }

    /// <example>Malawi</example>
    public string? scSpatialCoverage { get; set; }

    /// <example>ny</example>
    public string? scInLanguage { get; set; }

    /// <summary>Creator organisation</summary>
    public OrgNode? scCreator { get; set; }

    /// <summary>Publisher organisation</summary>
    public OrgNode? scPublisher { get; set; }

    /// <example>LicenseRef-InternalDemoOnly</example>
    public string? scLicense { get; set; }

    /// <example>v1.0.0</example>
    public string? scVersion { get; set; }

    /// <example>false</example>
    public bool crIsLiveDataset { get; set; }

    /// <summary>File objects (cr:FileObject) in this dataset</summary>
    public FileObjectNode[] distribution { get; set; } = [];
}

/// <summary>A schema.org Organisation reference.</summary>
public class OrgNode
{
    /// <example>sc:Organization</example>
    public string @type { get; set; } = "sc:Organization";

    /// <example>Nation Publications Limited</example>
    public string? scName { get; set; }
}

/// <summary>A cr:FileObject node representing one PDF.</summary>
public class FileObjectNode
{
    /// <example>cr:FileObject</example>
    public string @type { get; set; } = "cr:FileObject";

    /// <example>nation-2024/nation-2024-01-15.pdf</example>
    public string @id { get; set; } = "";

    /// <example>nation-2024-01-15.pdf</example>
    public string? scName { get; set; }

    /// <example>https://localhost:5001/api/files/file-2024-001005c4</example>
    public string? scContentUrl { get; set; }

    /// <example>application/pdf</example>
    public string? scEncodingFormat { get; set; }

    /// <example>204800</example>
    public long? scContentSize { get; set; }

    /// <example>abc123def456...</example>
    public string? crSha256 { get; set; }

    /// <example>John Banda</example>
    public string? scAuthor { get; set; }

    /// <example>2024-01-15</example>
    public string? scDatePublished { get; set; }

    /// <example>2024-01-16</example>
    public string? scDateModified { get; set; }

    /// <example>ny</example>
    public string? scInLanguage { get; set; }

    /// <example>4</example>
    public int scNumberOfPages { get; set; }

    /// <example>1500</example>
    public int? scWordCount { get; set; }

    /// <example>120</example>
    public int? ddpvTokenCount { get; set; }

    /// <example>8500</example>
    public int? ddpvCharCount { get; set; }

    /// <example>news</example>
    public string? dctSubject { get; set; }

    /// <example>news article</example>
    public string? olacDiscourseType { get; set; }

    /// <summary>Keywords associated with this file</summary>
    public string[] scKeywords { get; set; } = [];

    /// <summary>UNBIS themes associated with this file</summary>
    public string[] dcatTheme { get; set; } = [];

    /// <example>https://vocabularyserver.com/unbis/en/index.php</example>
    public string? dcatThemeTaxonomy { get; set; }
}

// ── Other response shapes ──────────────────────────────────────────────────

/// <summary>Returned when no files match the query filters.</summary>
public class NoMatchResponse
{
    /// <example>No files matched the supplied filters.</example>
    public string message { get; set; } = "";

    /// <example>0</example>
    public int totalMatched { get; set; }
}

/// <summary>Validation error response.</summary>
public class ErrorResponse
{
    /// <example>limit must be between 1 and 100.</example>
    public string error { get; set; } = "";
}

/// <summary>Authentication/authorisation error response.</summary>
public class AuthErrorResponse
{
    /// <example>Unauthorized</example>
    public string error { get; set; } = "";

    /// <example>Missing 'X-Api-Key' header.</example>
    public string detail { get; set; } = "";
}

/// <summary>Health check response.</summary>
public class HealthResponse
{
    /// <example>ok</example>
    public string status { get; set; } = "";

    /// <example>2026-03-02T12:00:00Z</example>
    public DateTime timestamp { get; set; }

    /// <example>1542</example>
    public int totalFiles { get; set; }
}

// ── Datasets Summary response ──────────────────────────────────────────────────

/// <summary>A publisher/creator with file count.</summary>
public class PublisherSummary
{
    /// <example>Nation Publications Limited</example>
    public string name { get; set; } = "";

    /// <example>1542</example>
    public int fileCount { get; set; }
}

/// <summary>A keyword or theme with occurrence count and distinct file count.</summary>
public class KeywordThemeSummary
{
    /// <example>malaria</example>
    public string value { get; set; } = "";

    /// <example>45</example>
    public int count { get; set; }

    /// <example>23</example>
    public int fileCount { get; set; }
}

/// <summary>Summary of text (Nation newspaper) pipeline.</summary>
public class TextPipelineSummary
{
    /// <example>5</example>
    public int datasetCount { get; set; }

    /// <example>1542</example>
    public int fileCount { get; set; }

    /// <example>1200</example>
    public int taggedFileCount { get; set; }
}

/// <summary>Summary of sound (audio) pipeline.</summary>
public class SoundPipelineSummary
{
    /// <example>2</example>
    public int datasetCount { get; set; }

    /// <example>500</example>
    public int fileCount { get; set; }

    /// <example>0</example>
    public int taggedFileCount { get; set; }
}

/// <summary>Aggregated summary of all datasets and files.</summary>
public class DatasetsSummaryResponse
{
    /// <example>2026-03-02T12:00:00Z</example>
    public DateTime generatedAt { get; set; }

    /// <example>7</example>
    public int totalDatasets { get; set; }

    /// <example>2042</example>
    public int totalFiles { get; set; }

    /// <example>1200</example>
    public int totalTaggedFiles { get; set; }

    /// <summary>Top publishers/creators by file count (sorted descending)</summary>
    public List<PublisherSummary> topPublishers { get; set; } = [];

    /// <summary>Top 20 keywords (sorted by count descending)</summary>
    public List<KeywordThemeSummary> topKeywords { get; set; } = [];

    /// <summary>Top 20 themes (sorted by count descending)</summary>
    public List<KeywordThemeSummary> topThemes { get; set; } = [];

    /// <summary>Breakdown of text pipeline metrics</summary>
    public TextPipelineSummary textSummary { get; set; } = new();

    /// <summary>Breakdown of sound pipeline metrics</summary>
    public SoundPipelineSummary soundSummary { get; set; } = new();
}
