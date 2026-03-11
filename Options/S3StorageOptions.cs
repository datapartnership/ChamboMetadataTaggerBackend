namespace MetadataTagger.Options
{
    public class S3StorageOptions
    {
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        /// <summary>
        /// Service URL for S3-compatible storage (e.g. SeaweedFS, MinIO).
        /// Leave empty to use AWS S3 standard endpoints.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;
        public string Region { get; set; } = "us-east-1";
        /// <summary>
        /// Use path-style URLs (required for SeaweedFS and MinIO).
        /// </summary>
        public bool ForcePathStyle { get; set; } = true;
    }
}
