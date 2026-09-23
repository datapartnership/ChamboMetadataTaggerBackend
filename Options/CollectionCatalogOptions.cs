namespace MetadataTagger.Options;

/// <summary>
/// Configures the "Collections" catalog — a table rebuilt from an Excel workbook stored in
/// blob storage (see <see cref="CatalogStorageOptions.Azure"/>.CollectionsContainerName,
/// <see cref="CatalogStorageOptions.S3"/>.CollectionsBucketName, or
/// <see cref="CatalogStorageOptions"/>.CollectionsFileStorageRoot for the storage location).
/// Ported from the LRLL merge.
/// </summary>
public class CollectionCatalogOptions
{
    public const string SectionName = "CollectionCatalog";

    /// <summary>Whether the catalog should be (re)synced from the Excel workbook on startup
    /// and via the manual sync endpoint. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Path/key of the Excel workbook within the configured collections storage
    /// location, e.g. "CollectionsMicrosoft.xlsx".</summary>
    public string BlobPath { get; set; } = "CollectionsMicrosoft.xlsx";

    /// <summary>Name of the worksheet to read. Leave empty to use the first worksheet.</summary>
    public string SheetName { get; set; } = "";
}
