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

    public LocalFileStorageService(IOptions<CatalogStorageOptions> storageOptions)
    {
        _storageOptions = storageOptions;
    }

    private string StorageRoot
    {
        get
        {
            var root = _storageOptions.Value.FileStorageRoot;
            return string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root;
        }
    }

    private string ResolvePath(string key)
    {
        var root = Path.GetFullPath(StorageRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, key));

        // Guard against path traversal
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path traversal detected for key '{key}'.");

        return fullPath;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(key);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found on disk: '{fullPath}'.", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public async Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default)
    {
        var stream = await OpenReadAsync(key, ct);
        stream.Seek(offset, SeekOrigin.Begin);
        return new BoundedReadStream(stream, length);
    }

    public Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default)
    {
        var info = new FileInfo(ResolvePath(key));
        return Task.FromResult(info.Exists
            ? new StorageFileEntry(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc))
            : null);
    }

    public Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

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

    /// <summary>Read-only view over the next <c>length</c> bytes of a stream.</summary>
    private sealed class BoundedReadStream(Stream inner, long length) : Stream
    {
        private readonly long _length = length;
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _length - _remaining;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Consume(inner.Read(buffer, offset, Limit(count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Consume(await inner.ReadAsync(buffer[..Limit(buffer.Length)], ct));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private int Limit(int count) => (int)Math.Min(count, _remaining);

        private int Consume(int read)
        {
            _remaining -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
