namespace MetadataTagging.DTOs;

/// <summary>All query parameters accepted by GET /api/datasets/query</summary>
public class DatasetQueryParams
{
    /// <summary>Filter by keyword (partial, case-insensitive)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by theme (partial, case-insensitive)</summary>
    public string? Theme { get; set; }

    /// <summary>Filter by author name (partial, case-insensitive)</summary>
    public string? Author { get; set; }

    /// <summary>Filter by publication year, e.g. 2024</summary>
    public string? Year { get; set; }

    /// <summary>
    /// Maximum number of file entries returned in the JSON-LD output.
    /// When omitted, filtered queries default to 30 entries and unfiltered
    /// queries return a random sample of 80% of all files. Max: 100 when supplied.
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>Query parameters accepted by GET /api/sound/query</summary>
public class SoundDatasetQueryParams
{
    /// <summary>Filter by subject language code or display language identifier (partial, case-insensitive), e.g. <c>ny</c> or <c>en-GB</c></summary>
    public string? Language { get; set; }

    /// <summary>Filter by programme / dataset name (partial, case-insensitive)</summary>
    public string? Programme { get; set; }

    /// <summary>Filter by keyword stored on the dataset (partial, case-insensitive)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by year appearing in datePublished or temporalCoverage, e.g. 2025</summary>
    public int? Year { get; set; }

    /// <summary>
    /// Maximum number of audio file entries returned in the JSON-LD output.
    /// When omitted, filtered queries default to 30 entries and unfiltered
    /// queries return a random sample of 80% of all files. Max: 100 when supplied.
    /// </summary>
    public int? Limit { get; set; }
}
