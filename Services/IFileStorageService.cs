namespace MetadataTagging.Services;

/// <summary>Represents a file entry returned by storage listing.</summary>
/// <param name="Key">Path relative to the storage root (e.g. <c>MediaHouseA/Sub/file.pdf</c>).</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="LastModified">Last-modified timestamp.</param>
/// <param name="ETag">Optional ETag / checksum provided by the storage backend.</param>
public record StorageFileEntry(string Key, long Size, DateTimeOffset LastModified, string? ETag = null);

/// <summary>
/// Abstracts file retrieval so the API can serve files from local disk
/// or any S3-compatible object store (SeaweedFS, MinIO, etc.).
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Opens a readable stream for the given storage key (relative path or S3 object key).
    /// </summary>
    /// <param name="key">Relative path or object key as stored in <c>FileRecord.ContentUrl</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open <see cref="Stream"/> backed by the storage provider.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the object does not exist.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Lists all files under the given prefix, recursively.
    /// Returns paths relative to the storage root.
    /// </summary>
    /// <param name="prefix">Optional path prefix to restrict the listing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default);
}
