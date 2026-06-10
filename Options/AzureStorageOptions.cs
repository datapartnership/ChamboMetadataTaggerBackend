namespace MetadataTagger.Options
{
    public class AzureStorageOptions
    {
        public bool UseManagedIdentity { get; set; } = false;
        public string ManagedIdentityClientId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
    }
}