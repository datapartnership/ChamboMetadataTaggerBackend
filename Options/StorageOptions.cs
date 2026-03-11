namespace MetadataTagger.Options;

public class StorageOptions
{
    public const string Section = "Storage";
    public string Provider { get; set; } = "s3";
    public string FileStorageRoot { get; set; } = string.Empty;
    public S3StorageOptions S3 { get; set; } = new();
    public AzureStorageOptions AzureStorage { get; set; } = new();
}
