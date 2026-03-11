using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public interface IStorageService
{
    Task<IEnumerable<BlobFileDto>> ListBlobsAsync();
    Task<string> GetBlobUrlAsync(string blobName);
    Task<string> GetBlobSasUrlAsync(string blobName, int expiryMinutes = 60);
    Task<bool> BlobExistsAsync(string blobName);
    Task<(Stream Content, string ContentType, long ContentLength)> DownloadBlobAsync(string blobName);
    Task<(Stream Content, string ContentType, long ContentLength, long TotalSize)> DownloadBlobRangeAsync(string blobName, long offset, long? length);
    Task<long> GetBlobSizeAsync(string blobName);
}
