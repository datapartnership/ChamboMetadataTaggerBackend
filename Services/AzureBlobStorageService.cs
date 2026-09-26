using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
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
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(IOptions<CatalogStorageOptions> storageOptions, ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        var azure = storageOptions.Value.Azure;

        var containerName = azure.ContainerName;
        if (string.IsNullOrEmpty(containerName))
            throw new InvalidOperationException("CatalogStorage:Azure:ContainerName is required.");

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
                "Either CatalogStorage:Azure:ConnectionString or CatalogStorage:Azure:ServiceUri must be provided.");
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
            throw NotFound(key);
        }
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.GetBlobClient(key).DownloadStreamingAsync(
                new BlobDownloadOptions { Range = new HttpRange(offset, length) }, ct);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw NotFound(key);
        }
    }

    /// <inheritdoc/>
    public async Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var properties = (await _container.GetBlobClient(key).GetPropertiesAsync(cancellationToken: ct)).Value;
            return new StorageFileEntry(key, properties.ContentLength, properties.LastModified, properties.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Signs with the account key when the connection string has one; otherwise requests a
    /// user delegation key, which needs the <c>Storage Blob Delegator</c> role (included in
    /// <c>Storage Blob Data Reader</c>/<c>Contributor</c>).
    /// </remarks>
    public async Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(key);
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry),
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        if (blobClient.CanGenerateSasUri)
            return blobClient.GenerateSasUri(sas).ToString();

        try
        {
            var service = _container.GetParentBlobServiceClient();
            var delegationKey = await service.GetUserDelegationKeyAsync(startsOn, sas.ExpiresOn, ct);
            return new BlobUriBuilder(blobClient.Uri)
            {
                Sas = sas.ToSasQueryParameters(delegationKey.Value, service.AccountName),
            }.ToUri().ToString();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Could not create a user delegation SAS for '{Key}'; returning the unsigned blob URL.", key);
            return blobClient.Uri.ToString();
        }
    }

    private FileNotFoundException NotFound(string key) =>
        new($"Blob '{key}' not found in container '{_container.Name}'.", key);
}
