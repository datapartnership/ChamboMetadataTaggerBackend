namespace MetadataTagging.Models;

/// <summary>
/// A single row of the "Collections" catalog, sourced from the Excel workbook stored in
/// blob storage (see <see cref="MetadataTagger.Options.CollectionCatalogOptions"/>). The entire
/// table is wiped and repopulated each time the workbook is synced, so <see cref="Id"/>
/// is a synthetic key rather than one derived from the source data (row <c>Name</c> values
/// are not unique). Ported from the LRLL merge.
/// </summary>
public class CollectionEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Provider { get; set; }
    public string? Language { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public double? Hours { get; set; }
    public int? Items { get; set; }
    public string? Description { get; set; }
    public int? StartYear { get; set; }
    public int? EndYear { get; set; }
    /// <summary>Comma-separated theme list, as authored in the workbook.</summary>
    public string? Themes { get; set; }
    /// <summary>Whether the data has been collected: "Yes", "No", or "Partly".</summary>
    public string? Collected { get; set; }
    /// <summary>Processing status: "Not Started", "In Progress", or "Completed".</summary>
    public string? Processing { get; set; }
    /// <summary>Whether the collection is available: "Yes" or "No".</summary>
    public string? Availability { get; set; }
    /// <summary>Free-text comments from the workbook. When populated, this holds the blob
    /// storage path where the collection's underlying data resides.</summary>
    public string? Comments { get; set; }
    public string? TaggedForText { get; set; }

    /// <summary>Whether <see cref="Comments"/> was confirmed to exist as a blob prefix in the
    /// relevant storage container(s) during the last sync. Null when <see cref="Comments"/>
    /// is empty (no path to check) or the check could not be performed.</summary>
    public bool? StoragePathVerified { get; set; }

    /// <summary>UTC timestamp of the sync run that produced this row.</summary>
    public DateTime SyncedAtUtc { get; set; }
}
