namespace MetadataTagger.Options;

/// <summary>
/// Storage configuration for the public catalog API (ported from the LRLL merge): serves
/// text file downloads, sound (FLAC) file downloads, and the Collections catalog Excel
/// workbook. Deliberately kept separate from <see cref="StorageOptions"/>, which continues
/// to configure storage for Chambo's own file-tagging inbox.
/// </summary>
public class CatalogStorageOptions
{
    public const string SectionName = "CatalogStorage";

    public string Provider { get; set; } = "local";
    public string FileStorageRoot { get; set; } = "";
    /// <summary>Root path for sound (FLAC) files when using the local provider.</summary>
    public string SoundFileStorageRoot { get; set; } = "";
    /// <summary>Root path for the collections catalog Excel file when using the local provider.</summary>
    public string CollectionsFileStorageRoot { get; set; } = "";
    public CatalogS3Options S3 { get; set; } = new();
    public CatalogAzureOptions Azure { get; set; } = new();
}

public class CatalogS3Options
{
    public string ServiceUrl { get; set; } = "";
    public string BucketName { get; set; } = "";
    /// <summary>Bucket name for sound (FLAC) files. Credentials and endpoint are shared with BucketName.</summary>
    public string SoundBucketName { get; set; } = "";
    /// <summary>Bucket name for the collections catalog Excel file. Credentials and endpoint are shared with BucketName.</summary>
    public string CollectionsBucketName { get; set; } = "";
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
    /// <summary>Client ID of a user-assigned managed identity. Leave empty for system-assigned
    /// managed identity or developer credentials (Azure CLI, VS, etc.).</summary>
    public string ManagedIdentityClientId { get; set; } = "";
    /// <summary>Container name for sound (FLAC) files. Credentials and service URI are shared with ContainerName.</summary>
    public string SoundContainerName { get; set; } = "";
    /// <summary>Container name for the collections catalog Excel file. Credentials and service URI are shared with ContainerName.</summary>
    public string CollectionsContainerName { get; set; } = "";
}
