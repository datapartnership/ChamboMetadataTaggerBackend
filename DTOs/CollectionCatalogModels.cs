namespace MetadataTagging.DTOs;

// ── Collections catalog query params ──────────────────────────────────────────

/// <summary>Pagination + filter parameters for GET /api/catalog/collections</summary>
public class CollectionCatalogQueryParams
{
    /// <summary>Full-text search across name, provider, and description (partial, case-insensitive)</summary>
    public string? Q { get; set; }

    /// <summary>Filter by type, e.g. "Text", "Audio", "Video" (partial, case-insensitive)</summary>
    public string? Type { get; set; }

    /// <summary>Filter by language (partial, case-insensitive)</summary>
    public string? Language { get; set; }

    /// <summary>Filter by provider (partial, case-insensitive)</summary>
    public string? Provider { get; set; }

    /// <summary>Filter by theme (partial, case-insensitive)</summary>
    public string? Theme { get; set; }

    /// <summary>Filter by collection status: "Yes", "No", or "Partly"</summary>
    public string? Collected { get; set; }

    /// <summary>Filter by processing status: "Not Started", "In Progress", or "Completed"</summary>
    public string? Processing { get; set; }

    /// <summary>Filter by availability: "Yes" or "No"</summary>
    public string? Availability { get; set; }

    /// <summary>Filter to collections whose Start/End Year range includes this year</summary>
    public int? Year { get; set; }

    /// <summary>1-based page number. Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Results per page (1–50). Default: 12.</summary>
    public int PageSize { get; set; } = 12;
}

// ── Collections catalog responses ─────────────────────────────────────────────

/// <summary>A single collection row in the catalog list.</summary>
public class CollectionCatalogListItem
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string? provider { get; set; }
    public string? language { get; set; }
    public string? type { get; set; }
    public string? format { get; set; }
    public double? hours { get; set; }
    public int? items { get; set; }
    public int? startYear { get; set; }
    public int? endYear { get; set; }
    public List<string> themes { get; set; } = [];
    public string? collected { get; set; }
    public string? processing { get; set; }
    public string? availability { get; set; }
    public string? taggedForText { get; set; }
    /// <summary>True when the source workbook's Comments column defines a blob storage path
    /// for this collection's underlying data.</summary>
    public bool hasStoragePath { get; set; }
    /// <summary>True/false when the storage path was checked against the relevant blob
    /// container(s) during the last sync; null when there was no path to check.</summary>
    public bool? storagePathVerified { get; set; }
}

/// <summary>Full detail for a single collection, returned by GET /api/catalog/collections/{id}</summary>
public class CollectionCatalogDetail
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string? provider { get; set; }
    public string? language { get; set; }
    public string? type { get; set; }
    public string? format { get; set; }
    public double? hours { get; set; }
    public int? items { get; set; }
    public string? description { get; set; }
    public int? startYear { get; set; }
    public int? endYear { get; set; }
    public List<string> themes { get; set; } = [];
    public string? collected { get; set; }
    public string? processing { get; set; }
    public string? availability { get; set; }
    public string? taggedForText { get; set; }
    /// <summary>Blob storage path where this collection's underlying data resides, when known.</summary>
    public string? storagePath { get; set; }
    /// <summary>True/false when <see cref="storagePath"/> was checked against the relevant
    /// blob container(s) during the last sync; null when there was no path to check.</summary>
    public bool? storagePathVerified { get; set; }
    public DateTime syncedAtUtc { get; set; }
}

/// <summary>Facet counts for the collections catalog sidebar filters.</summary>
public class CollectionCatalogFacetsResponse
{
    public int totalCollections { get; set; }
    public List<FacetItem> types { get; set; } = [];
    public List<FacetItem> languages { get; set; } = [];
    public List<FacetItem> providers { get; set; } = [];
    public List<FacetItem> themes { get; set; } = [];
    public List<FacetItem> collected { get; set; } = [];
    public List<FacetItem> processing { get; set; } = [];
    public List<FacetItem> availability { get; set; } = [];
}

/// <summary>Result returned by a manual or startup catalog sync.</summary>
public class CollectionCatalogSyncResult
{
    public bool success { get; set; }
    public int rowsSynced { get; set; }
    public int rowsSkipped { get; set; }
    public DateTime syncedAtUtc { get; set; }
    public string sourceBlobPath { get; set; } = "";
    public string? error { get; set; }
}
