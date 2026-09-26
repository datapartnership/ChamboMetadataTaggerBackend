namespace MetadataTagging.Services;

/// <summary>Represents a file entry returned by storage listing.</summary>
/// <param name="Key">Path relative to the storage root (e.g. <c>MediaHouseA/Sub/file.pdf</c>).</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="LastModified">Last-modified timestamp.</param>
/// <param name="ETag">Optional ETag / checksum provided by the storage backend.</param>
public record StorageFileEntry(string Key, long Size, DateTimeOffset LastModified, string? ETag = null);

/// <summary>
/// Abstracts catalog storage (local disk, any S3-compatible object store, or Azure Blob Storage).
/// It holds every collection as <c>Provider/CollectionFolder/...</c> and is used both by the
/// tagging workflow and by the catalog API.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Opens a readable stream for the given storage key (relative path or S3 object key).
    /// </summary>
    /// <param name="key">Relative path or object key as stored in <c>CollectionFile.ContentUrl</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open <see cref="Stream"/> backed by the storage provider.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the object does not exist.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Opens a stream over <paramref name="length"/> bytes of the file starting at <paramref name="offset"/>.
    /// The caller is responsible for keeping the range inside the file.
    /// </summary>
    /// <exception cref="FileNotFoundException">Thrown when the object does not exist.</exception>
    Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default);

    /// <summary>
    /// Lists all files under the given prefix, recursively.
    /// Returns paths relative to the storage root.
    /// </summary>
    /// <param name="prefix">Optional path prefix to restrict the listing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default);

    /// <summary>Returns the entry for a single file, or <c>null</c> when it does not exist.</summary>
    Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Returns a time-limited URL that grants read access to the file without further
    /// authentication (Azure SAS / S3 presigned URL), or <c>null</c> when the provider cannot
    /// issue one (local disk).
    /// </summary>
    Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}
