using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

/// <summary>
/// Serves files from Azure Blob Storage.
/// Authentication: uses <see cref="CatalogAzureOptions.ConnectionString"/> when set;
/// otherwise uses <see cref="CatalogAzureOptions.ServiceUri"/> with <see cref="DefaultAzureCredential"/>
/// (workload identity, managed identity, developer CLI, etc.).
/// Configuration is read from <see cref="CatalogStorageOptions.Azure"/>.
/// </summary>
public sealed class AzureBlobStorageService : IFileStorageService
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(IOptions<CatalogStorageOptions> storageOptions, string? containerOverride = null)
    {
        var azure = storageOptions.Value.Azure;

        var containerName = string.IsNullOrEmpty(containerOverride) ? azure.ContainerName : containerOverride;
        if (string.IsNullOrEmpty(containerName))
            throw new InvalidOperationException("Storage:Azure:ContainerName (or sound container override) is required.");

        if (!string.IsNullOrEmpty(azure.ConnectionString))
        {
            _container = new BlobContainerClient(azure.ConnectionString, containerName);
        }
        else if (!string.IsNullOrEmpty(azure.ServiceUri))
        {
            var containerUri = new Uri($"{azure.ServiceUri.TrimEnd('/')}/{containerName}");
            var credentialOptions = string.IsNullOrEmpty(azure.ManagedIdentityClientId)
                ? new DefaultAzureCredentialOptions()
                : new DefaultAzureCredentialOptions { ManagedIdentityClientId = azure.ManagedIdentityClientId };
            _container = new BlobContainerClient(containerUri, new DefaultAzureCredential(credentialOptions));
        }
        else
        {
            throw new InvalidOperationException(
                "Either Storage:Azure:ConnectionString or Storage:Azure:ServiceUri must be provided.");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default)
    {
        var entries = new List<StorageFileEntry>();

        // GetBlobsAsync returns an AsyncPageable — the SDK handles pagination internally.
        var blobs = _container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: string.IsNullOrEmpty(prefix) ? null : prefix,
            cancellationToken: ct);

        await foreach (BlobItem blob in blobs)
        {
            entries.Add(new StorageFileEntry(
                Key:          blob.Name,
                Size:         blob.Properties.ContentLength ?? 0,
                LastModified: blob.Properties.LastModified ?? default,
                ETag:         blob.Properties.ETag?.ToString()));
        }

        return entries;
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(key);

        try
        {
            // OpenReadAsync fetches blob properties up-front (to populate Length) and then
            // streams content via discrete range-GET requests.  The returned
            // LazyLoadingReadOnlyStream is genuinely seekable with a valid Length, so:
            //   • ASP.NET Core's FileStreamResult emits a Content-Length response header.
            //   • EOF is deterministic — each chunk read is a bounded range request, so
            //     ReadAsync returns 0 as soon as the chunk is exhausted rather than
            //     spinning inside a RetriableStream wrapper waiting for the connection to
            //     close (the behaviour of DownloadStreamingAsync that caused client hangs).
            return await blobClient.OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException(
                $"Blob '{key}' not found in container '{_container.Name}'.", key);
        }
    }
}
