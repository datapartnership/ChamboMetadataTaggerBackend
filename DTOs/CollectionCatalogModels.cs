namespace MetadataTagging.DTOs;

// ── Collections catalog query params ──────────────────────────────────────────

/// <summary>Pagination + filter parameters for GET /api/catalog/collections</summary>
public class CollectionCatalogQueryParams
{
    /// <summary>Full-text search across name, provider, and description (partial, case-insensitive)</summary>
    public string? Q { get; set; }

    /// <summary>Filter by media type: <c>Text</c>, <c>Audio</c>, or <c>Video</c></summary>
    public string? MediaType { get; set; }

    /// <summary>Filter by language name or code (partial, case-insensitive)</summary>
    public string? Language { get; set; }

    /// <summary>Filter by provider (partial, case-insensitive)</summary>
    public string? Provider { get; set; }

    /// <summary>Filter by theme (partial, case-insensitive)</summary>
    public string? Theme { get; set; }

    /// <summary>Filter to collections whose Start/End Year range includes this year</summary>
    public int? Year { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–50). Default: 12.</summary>
    public int PageSize { get; set; } = 12;
}

/// <summary>Pagination + filter parameters for GET /api/catalog/collections/{id}/files</summary>
public class CollectionFileQueryParams
{
    /// <summary>Search across file name and author (partial, case-insensitive)</summary>
    public string? Q { get; set; }

    /// <summary>Filter by file kind: <c>Text</c>, <c>Audio</c>, <c>Video</c>, or <c>Transcription</c></summary>
    public string? Kind { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–100). Default: 25.</summary>
    public int PageSize { get; set; } = 25;
}

// ── Collections catalog responses ─────────────────────────────────────────────

/// <summary>A single collection row in the catalog list.</summary>
public class CollectionCatalogListItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string provider { get; set; } = "";
    public string mediaType { get; set; } = "";
    public string? language { get; set; }
    public string? languageCode { get; set; }
    public List<string> formats { get; set; } = [];
    public double? hours { get; set; }
    /// <summary>Number of primary (non-transcription) files.</summary>
    public int items { get; set; }
    public int? startYear { get; set; }
    public int? endYear { get; set; }
    public List<string> themes { get; set; } = [];
}

/// <summary>Full detail for a single collection, returned by GET /api/catalog/collections/{id}</summary>
public class CollectionCatalogDetail : CollectionCatalogListItem
{
    public string? description { get; set; }
    public List<string> keywords { get; set; } = [];
    public string storagePath { get; set; } = "";
    public string? temporalCoverage { get; set; }
    public string? datePublished { get; set; }
    public string? dateModified { get; set; }
    public string? license { get; set; }
    public string? creator { get; set; }
    public string? publisher { get; set; }
    public string? sourceOrganization { get; set; }
    public string? version { get; set; }
    public bool piiScreening { get; set; }
    public bool sensitiveContent { get; set; }
    public int transcriptionCount { get; set; }
    public DateTime seededAtUtc { get; set; }
}

/// <summary>A file row returned by GET /api/catalog/collections/{id}/files</summary>
public class CollectionFileItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string kind { get; set; } = "";
    public string encodingFormat { get; set; } = "";
    public long? contentSize { get; set; }
    public string? dateModified { get; set; }
    public string? author { get; set; }
    public string? datePublished { get; set; }
    public int? wordCount { get; set; }
    public int? numberOfPages { get; set; }
    public double? duration { get; set; }
    public double? sampleRate { get; set; }
    public int? channels { get; set; }
    public double? bitrate { get; set; }
    public string? transcriptionFileId { get; set; }
    public List<string> keywords { get; set; } = [];
    public List<string> themes { get; set; } = [];
}

/// <summary>Facet counts for the collections catalog sidebar filters.</summary>
public class CollectionCatalogFacetsResponse
{
    public int totalCollections { get; set; }
    public List<FacetItem> mediaTypes { get; set; } = [];
    public List<FacetItem> languages { get; set; } = [];
    public List<FacetItem> providers { get; set; } = [];
    public List<FacetItem> themes { get; set; } = [];
    /// <summary>Years covered by at least one collection, newest first.</summary>
    public List<FacetItem> years { get; set; } = [];
}
