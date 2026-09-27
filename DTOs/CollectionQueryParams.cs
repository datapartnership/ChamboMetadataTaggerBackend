namespace MetadataTagging.DTOs;

/// <summary>All query parameters accepted by GET /api/collections/query</summary>
public class CollectionQueryParams
{
    /// <summary>Filter by media type: <c>Text</c>, <c>Audio</c>, or <c>Video</c></summary>
    public string? MediaType { get; set; }

    /// <summary>Restrict to a single collection</summary>
    public string? CollectionId { get; set; }

    /// <summary>Filter by provider (partial, case-insensitive)</summary>
    public string? Provider { get; set; }

    /// <summary>Filter by collection language name or code (partial, case-insensitive), e.g. <c>nya</c></summary>
    public string? Language { get; set; }

    /// <summary>Filter by file or collection keyword (partial, case-insensitive)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by file or collection theme (partial, case-insensitive)</summary>
    public string? Theme { get; set; }

    /// <summary>Filter by text file author (partial, case-insensitive)</summary>
    public string? Author { get; set; }

    /// <summary>Filter by year: a text file's publication year, or a year inside the collection's coverage</summary>
    public int? Year { get; set; }

    /// <summary>
    /// Maximum number of file entries returned in the JSON-LD output.
    /// When omitted, filtered queries default to 30 entries and unfiltered
    /// queries return a random sample of 80% of all files. Max: 100 when supplied.
    /// </summary>
    public int? Limit { get; set; }
}
