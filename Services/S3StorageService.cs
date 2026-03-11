using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

public class S3StorageService : IStorageService
{
    private readonly AmazonS3Client _s3Client;
    private readonly string _bucketName;
    private readonly string _serviceUrl;

    public S3StorageService(IOptions<S3StorageOptions> options)
    {
        var settings = options.Value;
        _bucketName = settings.BucketName;
        _serviceUrl = settings.ServiceUrl.TrimEnd('/');

        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.ForcePathStyle
        };

        if (!string.IsNullOrEmpty(settings.ServiceUrl))
        {
            config.ServiceURL = settings.ServiceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        _s3Client = new AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
    }

    public async Task<IEnumerable<BlobFileDto>> ListBlobsAsync()
    {
        var blobs = new List<BlobFileDto>();

        try
        {
            var request = new ListObjectsV2Request { BucketName = _bucketName };
            ListObjectsV2Response response;

            do
            {
                response = await _s3Client.ListObjectsV2Async(request);

                foreach (var obj in response.S3Objects)
                {
                    var fileUrl = $"{_serviceUrl}/{_bucketName}/{obj.Key}";
                    blobs.Add(new BlobFileDto
                    {
                        BlobName = obj.Key,
                        FileUrl = fileUrl,
                        FileSize = obj.Size ?? 0,
                        ContentType = null,
                        FileCategory = FileCategoryHelper.FromContentType(null, obj.Key).ToString(),
                        LastModified = obj.LastModified
                    });
                }

                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error listing S3 objects: {ex.Message}", ex);
        }

        return blobs;
    }

    public Task<string> GetBlobUrlAsync(string blobName)
    {
        var url = $"{_serviceUrl}/{_bucketName}/{blobName}";
        return Task.FromResult(url);
    }

    public Task<string> GetBlobSasUrlAsync(string blobName, int expiryMinutes = 60)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = blobName,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }

    public async Task<bool> BlobExistsAsync(string blobName)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(_bucketName, blobName);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<(Stream Content, string ContentType, long ContentLength)> DownloadBlobAsync(string blobName)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, blobName);
        var contentType = response.Headers.ContentType ?? "application/octet-stream";
        var contentLength = response.ContentLength;

        return (response.ResponseStream, contentType, contentLength);
    }

    public async Task<(Stream Content, string ContentType, long ContentLength, long TotalSize)> DownloadBlobRangeAsync(
        string blobName, long offset, long? length)
    {
        var metadata = await _s3Client.GetObjectMetadataAsync(_bucketName, blobName);
        var totalSize = metadata.ContentLength;
        var contentType = metadata.Headers.ContentType ?? "application/octet-stream";

        var rangeEnd = length.HasValue ? offset + length.Value - 1 : totalSize - 1;
        if (rangeEnd >= totalSize) rangeEnd = totalSize - 1;

        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = blobName,
            ByteRange = new ByteRange(offset, rangeEnd)
        };

        var response = await _s3Client.GetObjectAsync(request);
        var actualLength = rangeEnd - offset + 1;

        return (response.ResponseStream, contentType, actualLength, totalSize);
    }

    public async Task<long> GetBlobSizeAsync(string blobName)
    {
        var metadata = await _s3Client.GetObjectMetadataAsync(_bucketName, blobName);
        return metadata.ContentLength;
    }
}
