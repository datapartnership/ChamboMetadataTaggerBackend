using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

/// <summary>
/// Serves files from any S3-compatible object store (SeaweedFS, MinIO, Ceph, AWS S3, …).
/// Configuration is read from <see cref="CatalogStorageOptions.S3"/>.
/// </summary>
public sealed class S3FileStorageService : IFileStorageService, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly Protocol _urlProtocol;

    public S3FileStorageService(IOptions<CatalogStorageOptions> storageOptions)
    {
        var s3 = storageOptions.Value.S3;

        if (string.IsNullOrEmpty(s3.ServiceUrl))
            throw new InvalidOperationException("CatalogStorage:S3:ServiceUrl is required.");

        if (string.IsNullOrEmpty(s3.BucketName))
            throw new InvalidOperationException("CatalogStorage:S3:BucketName is required.");

        _bucket = s3.BucketName;
        _urlProtocol = s3.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;

        var credentials = new BasicAWSCredentials(s3.AccessKey, s3.SecretKey);

        var s3Config = new AmazonS3Config
        {
            ServiceURL        = s3.ServiceUrl,
            ForcePathStyle    = s3.ForcePathStyle,
            AuthenticationRegion = s3.Region,
        };

        _client = new AmazonS3Client(credentials, s3Config);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default)
    {
        var entries = new List<StorageFileEntry>();
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix     = prefix,
                ContinuationToken = continuationToken,
            };

            var response = await _client.ListObjectsV2Async(request, ct);

            foreach (var obj in response.S3Objects)
                entries.Add(new StorageFileEntry(
                    Key:          obj.Key,
                    Size:         obj.Size ?? 0,
                    LastModified: obj.LastModified is { } lastModified ? new DateTimeOffset(lastModified) : default,
                    ETag:         obj.ETag?.Trim('"')));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return entries;
    }

    /// <inheritdoc/>
    // Strips leading './' or '/' so ContentUrls like "./Folder/file.pdf" become
    // the clean S3 object key "Folder/file.pdf".
    private static string NormalizeKey(string key)
    {
        var k = key.TrimStart('/');
        return k.StartsWith("./", StringComparison.Ordinal) ? k[2..] : k;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = NormalizeKey(key) }, ct);

    /// <inheritdoc/>
    public Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default) =>
        GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucket,
            Key        = NormalizeKey(key),
            ByteRange  = new ByteRange(offset, offset + length - 1),
        }, ct);

    private async Task<Stream> GetObjectAsync(GetObjectRequest request, CancellationToken ct)
    {
        GetObjectResponse response;
        try
        {
            response = await _client.GetObjectAsync(request, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Object '{request.Key}' not found in bucket '{_bucket}'.", request.Key);
        }

        // Wrap the S3 response stream so it is disposed together with the response object.
        return new S3ResponseStream(response);
    }

    /// <inheritdoc/>
    public async Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(_bucket, NormalizeKey(key), ct);
            return new StorageFileEntry(
                key,
                metadata.ContentLength,
                metadata.LastModified is { } lastModified ? new DateTimeOffset(lastModified) : default,
                metadata.ETag?.Trim('"'));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
        Task.FromResult<string?>(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key        = NormalizeKey(key),
            Verb       = HttpVerb.GET,
            Protocol   = _urlProtocol,
            Expires    = DateTime.UtcNow.Add(expiry),
        }));

    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Thin wrapper that keeps the <see cref="GetObjectResponse"/> alive
    /// (and therefore the underlying socket open) until the caller is done reading.
    /// </summary>
    private sealed class S3ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead  => _inner.CanRead;
        // S3 response body streams are non-seekable network streams, but the content length
        // is known from the HTTP response headers.  Reporting CanSeek = true lets
        // ASP.NET Core's FileStreamResult set a Content-Length header so clients know when
        // the body ends and don't hang waiting for the connection to close.
        // FileStreamResult.EnableRangeProcessing defaults to false, so Seek() is never
        // called for ordinary downloads.
        public override bool CanSeek  => true;
        public override bool CanWrite => false;
        public override long Length   => response.ContentLength;
        public override long Position
        {
            get => throw new NotSupportedException("S3 response streams do not support seeking.");
            set => throw new NotSupportedException("S3 response streams do not support seeking.");
        }

        public override void  Flush()                                      => _inner.Flush();
        public override int   Read(byte[] buffer, int offset, int count)   => _inner.Read(buffer, offset, count);
        public override long  Seek(long offset, SeekOrigin origin)         => _inner.Seek(offset, origin);
        public override void  SetLength(long value)                        => _inner.SetLength(value);
        public override void  Write(byte[] buffer, int offset, int count)  => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => _inner.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => _inner.ReadAsync(buffer, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
