namespace MetadataTagger.Options;

/// <summary>
/// The application's only file storage. A single bucket/container holds every collection,
/// laid out as <c>Provider/CollectionFolder</c>: taggers work on these files,
/// <c>POST /api/seed</c> scans them, and the catalog API serves them.
/// </summary>
public class CatalogStorageOptions
{
    public const string SectionName = "CatalogStorage";

    public string Provider { get; set; } = "local";
    public string FileStorageRoot { get; set; } = "";
    public CatalogS3Options S3 { get; set; } = new();
    public CatalogAzureOptions Azure { get; set; } = new();
}

public class CatalogS3Options
{
    public string ServiceUrl { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool ForcePathStyle { get; set; }
    public string Region { get; set; } = "us-east-1";
}

public class CatalogAzureOptions
{
    /// <summary>Use a connection string (e.g. "DefaultEndpointsProtocol=https;…") for key-based auth.</summary>
    public string ConnectionString { get; set; } = "";
    /// <summary>Base URI of the storage account (e.g. "https://&lt;account&gt;.blob.core.windows.net").
    /// Used together with DefaultAzureCredential when ConnectionString is empty.</summary>
    public string ServiceUri { get; set; } = "";
    public string ContainerName { get; set; } = "";
    /// <summary>
    /// Blob key prefix that sits in front of <c>Provider/Collection/...</c>,
    /// e.g. <c>markdown/CATALOGPROD</c>. Catalog keys stay relative to this prefix.
    /// Leave empty to use the container root.
    /// </summary>
    public string Prefix { get; set; } = "";
    /// <summary>Client ID of a user-assigned managed identity. Leave empty for system-assigned
    /// managed identity or developer credentials (Azure CLI, VS, etc.).</summary>
    public string ManagedIdentityClientId { get; set; } = "";
}
