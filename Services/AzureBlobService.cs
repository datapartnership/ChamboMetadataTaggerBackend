using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

public class AzureBlobService : IStorageService
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobService(IOptions<AzureStorageOptions> blobSettings)
    {
        var settings = blobSettings.Value;

        BlobServiceClient blobServiceClient;
        if (settings.UseManagedIdentity)
        {
            var serviceUri = new Uri($"https://{settings.AccountName}.blob.core.windows.net");
            blobServiceClient = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
        }
        else
        {
            blobServiceClient = new BlobServiceClient(settings.ConnectionString);
        }
        _containerClient = blobServiceClient.GetBlobContainerClient(settings.ContainerName);
    }

    public async Task<IEnumerable<BlobFileDto>> ListBlobsAsync()
    {
        var blobs = new List<BlobFileDto>();

        try
        {
            await foreach (BlobItem blobItem in _containerClient.GetBlobsAsync())
            {
                var blobClient = _containerClient.GetBlobClient(blobItem.Name);

                var contentType = blobItem.Properties.ContentType;
                blobs.Add(new BlobFileDto
                {
                    BlobName = blobItem.Name,
                    FileUrl = blobClient.Uri.ToString(),
                    FileSize = blobItem.Properties.ContentLength ?? 0,
                    ContentType = contentType,
                    FileCategory = MetadataTagging.Models.FileCategoryHelper.FromContentType(contentType, blobItem.Name).ToString(),
                    LastModified = blobItem.Properties.LastModified?.UtcDateTime
                });
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error listing blobs: {ex.Message}", ex);
        }

        return blobs;
    }

    public async Task<string> GetBlobUrlAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (await blobClient.ExistsAsync())
        {
            return blobClient.Uri.ToString();
        }

        throw new FileNotFoundException($"Blob '{blobName}' not found");
    }

    public async Task<string> GetBlobSasUrlAsync(string blobName, int expiryMinutes = 60)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found");
        }

        if (!blobClient.CanGenerateSasUri)
        {
            return blobClient.Uri.ToString();
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            BlobName = blobName,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        return sasUri.ToString();
    }

    public async Task<bool> BlobExistsAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);
        return await blobClient.ExistsAsync();
    }

    public async Task<(Stream Content, string ContentType, long ContentLength)> DownloadBlobAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found");
        }

        var response = await blobClient.DownloadStreamingAsync();
        var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
        var contentLength = response.Value.Details.ContentLength;

        return (response.Value.Content, contentType, contentLength);
    }

    public async Task<(Stream Content, string ContentType, long ContentLength, long TotalSize)> DownloadBlobRangeAsync(string blobName, long offset, long? length)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found");
        }

        var properties = await blobClient.GetPropertiesAsync();
        var totalSize = properties.Value.ContentLength;
        var contentType = properties.Value.ContentType ?? "application/octet-stream";

        var end = length.HasValue ? offset + length.Value - 1 : totalSize - 1;
        if (end >= totalSize) end = totalSize - 1;

        var range = new Azure.HttpRange(offset, end - offset + 1);
        var response = await blobClient.DownloadStreamingAsync(new BlobDownloadOptions { Range = range });

        return (response.Value.Content, contentType, end - offset + 1, totalSize);
    }

    public async Task<long> GetBlobSizeAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found");
        }

        var properties = await blobClient.GetPropertiesAsync();
        return properties.Value.ContentLength;
    }
}
