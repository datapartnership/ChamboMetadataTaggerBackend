namespace MetadataTagging.DTOs;

// ── Catalog query params ──────────────────────────────────────────────────────

/// <summary>Pagination + filter parameters for GET /api/catalog/datasets</summary>
public class CatalogDatasetQueryParams
{
    /// <summary>Full-text search across dataset name and publisher (partial, case-insensitive)</summary>
    public string? Q { get; set; }

    /// <summary>Filter by keyword (partial, case-insensitive)</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by theme (partial, case-insensitive)</summary>
    public string? Theme { get; set; }

    /// <summary>Filter by publication year, e.g. 2024</summary>
    public string? Year { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–50). Default: 12.</summary>
    public int PageSize { get; set; } = 12;
}

/// <summary>Pagination + filter parameters for GET /api/catalog/files</summary>
public class CatalogFileQueryParams
{
    /// <summary>Full-text search across file name and author (partial, case-insensitive).</summary>
    public string? Q { get; set; }

    /// <summary>Filter by keyword using exact value matching.</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by theme using exact value matching.</summary>
    public string? Theme { get; set; }

    /// <summary>Filter by year, e.g. 2024.</summary>
    public string? Year { get; set; }

    /// <summary>Filter by author name containing the supplied text.</summary>
    public string? Author { get; set; }

    /// <summary>Restrict files to a single dataset.</summary>
    public string? DatasetId { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–50). Default: 12.</summary>
    public int PageSize { get; set; } = 12;
}

/// <summary>Pagination + filter parameters for GET /api/catalog/sound/datasets</summary>
public class CatalogSoundQueryParams
{
    /// <summary>Full-text search across dataset name and creator (partial, case-insensitive)</summary>
    public string? Q { get; set; }

    /// <summary>Filter by subject language code or display language identifier (partial, case-insensitive)</summary>
    public string? Language { get; set; }

    /// <summary>Filter by publication year, e.g. 2024</summary>
    public string? Year { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–50). Default: 12.</summary>
    public int PageSize { get; set; } = 12;
}

// ── Catalog list responses ────────────────────────────────────────────────────

/// <summary>A single text dataset row in the catalog list.</summary>
public class CatalogDatasetListItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? publisher { get; set; }
    public string? creator { get; set; }
    public string? datePublished { get; set; }
    public string? temporalCoverage { get; set; }
    public string? spatialCoverage { get; set; }
    public string? languageCode { get; set; }
    public string? license { get; set; }
    public int fileCount { get; set; }
    public List<string> topKeywords { get; set; } = [];
    public List<string> topThemes { get; set; } = [];
}

/// <summary>A single sound dataset row in the catalog list.</summary>
public class CatalogSoundDatasetListItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? creator { get; set; }
    public string? provider { get; set; }
    public string? datePublished { get; set; }
    public string? temporalCoverage { get; set; }
    public string? spatialCoverage { get; set; }
    public string? subjectLanguageCode { get; set; }
    public string? subjectLanguageName { get; set; }
    public string? inLanguageName { get; set; }
    public string? license { get; set; }
    public string? description { get; set; }
    public int fileCount { get; set; }
    public List<string> keywords { get; set; } = [];
}

/// <summary>Paginated wrapper for any catalog list.</summary>
public class CatalogPagedResult<T>
{
    public int page { get; set; }
    public int pageSize { get; set; }
    public int totalCount { get; set; }
    public int totalPages { get; set; }
    public List<T> results { get; set; } = [];
}

// ── Catalog detail responses ──────────────────────────────────────────────────

/// <summary>Full text dataset detail returned by GET /api/catalog/datasets/{id}</summary>
public class CatalogDatasetDetail
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? publisher { get; set; }
    public string? creator { get; set; }
    public string? datePublished { get; set; }
    public string? dateModified { get; set; }
    public string? temporalCoverage { get; set; }
    public string? spatialCoverage { get; set; }
    public string? languageCode { get; set; }
    public string? license { get; set; }
    public string? version { get; set; }
    public int totalFiles { get; set; }
    public List<CatalogFileItem> files { get; set; } = [];
    public List<string> allKeywords { get; set; } = [];
    public List<string> allThemes { get; set; } = [];
}

/// <summary>A file entry within a dataset detail response.</summary>
public class CatalogFileItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? author { get; set; }
    public string? datePublished { get; set; }
    public string? encodingFormat { get; set; }
    public long? contentSize { get; set; }
    public int? wordCount { get; set; }
    public int? charCount { get; set; }
    public int? tokenCount { get; set; }
    public int numberOfPages { get; set; }
    public string? subject { get; set; }
    public string? discourseType { get; set; }
    public List<string> keywords { get; set; } = [];
    public List<string> themes { get; set; } = [];
}

/// <summary>A paginated file row used for file-centric catalog filters.</summary>
public class CatalogFileListItem
{
    public string id { get; set; } = "";
    public string datasetId { get; set; } = "";
    public string datasetName { get; set; } = "";
    public string name { get; set; } = "";
    public string? author { get; set; }
    public string? datePublished { get; set; }
    public string? encodingFormat { get; set; }
    public long? contentSize { get; set; }
    public int? wordCount { get; set; }
    public int? charCount { get; set; }
    public int? tokenCount { get; set; }
    public int numberOfPages { get; set; }
    public string? subject { get; set; }
    public string? discourseType { get; set; }
    public List<string> keywords { get; set; } = [];
    public List<string> themes { get; set; } = [];
}

/// <summary>Full sound dataset detail returned by GET /api/catalog/sound/datasets/{id}</summary>
public class CatalogSoundDatasetDetail
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? description { get; set; }
    public string? creator { get; set; }
    public string? provider { get; set; }
    public string? sourceOrganization { get; set; }
    public string? datePublished { get; set; }
    public string? dateCreated { get; set; }
    public string? dateModified { get; set; }
    public string? temporalCoverage { get; set; }
    public string? spatialCoverage { get; set; }
    public string? subjectLanguageCode { get; set; }
    public string? subjectLanguageName { get; set; }
    public string? inLanguageName { get; set; }
    public string? inLanguageIdentifier { get; set; }
    public string? license { get; set; }
    public string? version { get; set; }
    public bool piiScreening { get; set; }
    public bool sensitiveContent { get; set; }
    public List<string> keywords { get; set; } = [];
    public int totalFiles { get; set; }
    public List<CatalogSoundFileItem> files { get; set; } = [];
}

/// <summary>An audio file entry within a sound dataset detail response.</summary>
public class CatalogSoundFileItem
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? encodingFormat { get; set; }
    public long? contentSize { get; set; }
    public double duration { get; set; }
    public double sampleRate { get; set; }
    public int channels { get; set; }
    public double bitrate { get; set; }
    public double? integratedLoudness { get; set; }
    public double? signalToNoiseRatio { get; set; }
    public string? dateModified { get; set; }
}

// ── Facets response ───────────────────────────────────────────────────────────

/// <summary>Facet counts for the catalog sidebar filters.</summary>
public class CatalogFacetsResponse
{
    public int totalFiles { get; set; }
    public int taggedFiles { get; set; }
    public List<FacetItem> keywords { get; set; } = [];
    public List<FacetItem> themes { get; set; } = [];
    public List<FacetItem> authors { get; set; } = [];
    public List<string> years { get; set; } = [];
    public List<FacetItem> languages { get; set; } = [];
}

public class FacetItem
{
    public string value { get; set; } = "";
    public int count { get; set; }
}
