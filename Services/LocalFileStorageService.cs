using Microsoft.Extensions.Options;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

/// <summary>
/// Serves files from the local filesystem (original behaviour).
/// The storage root is resolved from <see cref="CatalogStorageOptions.FileStorageRoot"/>,
/// with env-var override support configured at startup.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly IOptions<CatalogStorageOptions> _storageOptions;
    private readonly string? _rootOverride;

    public LocalFileStorageService(IOptions<CatalogStorageOptions> storageOptions, string? rootOverride = null)
    {
        _storageOptions = storageOptions;
        _rootOverride = rootOverride;
    }

    private string StorageRoot
    {
        get
        {
            if (!string.IsNullOrEmpty(_rootOverride)) return _rootOverride;
            var root = _storageOptions.Value.FileStorageRoot;
            return string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(StorageRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, key));

        // Guard against path traversal
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path traversal detected for key '{key}'.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found on disk: '{fullPath}'.", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default)
    {
        var root = Path.GetFullPath(StorageRoot);
        var searchRoot = string.IsNullOrEmpty(prefix)
            ? root
            : Path.GetFullPath(Path.Combine(root, prefix));

        // Guard against path traversal
        if (!searchRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected in listing prefix.");

        if (!Directory.Exists(searchRoot))
            return Task.FromResult<IReadOnlyList<StorageFileEntry>>([]);

        var entries = Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var key = Path.GetRelativePath(root, path).Replace('\\', '/');
                return new StorageFileEntry(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc));
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<StorageFileEntry>>(entries);
    }
}
