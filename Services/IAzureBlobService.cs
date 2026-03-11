namespace MetadataTagging.Services;

/// <summary>
/// Kept for backward compatibility. Prefer injecting <see cref="IStorageService"/> directly.
/// </summary>
public interface IAzureBlobService : IStorageService
{
}
