namespace MetadataTagging.DTOs;

/// <summary>Row changes applied by a seed run, for one media type or in total.</summary>
public class SeedCounts
{
    public int CollectionsAdded { get; set; }
    public int CollectionsUpdated { get; set; }
    public int CollectionsDeleted { get; set; }
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesDeleted { get; set; }
}

/// <summary>Summary returned by <c>POST /api/seed</c>.</summary>
public class SeedResult
{
    /// <summary>Changes per media type, keyed by <c>Text</c>, <c>Audio</c>, and <c>Video</c>.</summary>
    public Dictionary<string, SeedCounts> ByMediaType { get; set; } = [];

    /// <summary>Changes across all media types.</summary>
    public SeedCounts Totals { get; set; } = new();

    /// <summary>Number of catalog files (any media type) enriched with tags from a submitted or approved tagging.</summary>
    public int TaggerFilesMatched { get; set; }

    /// <summary>Submitted or approved tagger files whose storage key matches no catalog file, so their tags are not published.</summary>
    public int TaggedFilesNotCataloged { get; set; }

    /// <summary>Up to five storage keys counted in <see cref="TaggedFilesNotCataloged"/>.</summary>
    public List<string> TaggedFilesNotCatalogedExamples { get; set; } = [];

    /// <summary>Storage content that was skipped or looked inconsistent. The seed still completes.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; set; } = string.Empty;
}