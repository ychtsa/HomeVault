using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace HomeVault.Services;

/// <summary>Private blobs; callers must authorize the catalog before reading or writing.</summary>
public sealed class AzureBlobCatalogImageStorage(BlobContainerClient container) : ICatalogImageStorage
{
    public async Task SaveAsync(string catalogId, string itemId, Stream content)
    {
        // The private container is provisioned by Bicep, never made public by the app.
        await GetBlob(catalogId, itemId).UploadAsync(content, overwrite: true);
    }

    public async Task<Stream?> OpenReadAsync(string catalogId, string itemId)
    {
        try
        {
            var response = await GetBlob(catalogId, itemId).DownloadStreamingAsync();
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public void Delete(string catalogId, string itemId) =>
        GetBlob(catalogId, itemId).DeleteIfExists(DeleteSnapshotsOption.IncludeSnapshots);

    private BlobClient GetBlob(string catalogId, string itemId)
    {
        if (!IsSafeId(catalogId) || !IsSafeId(itemId))
            throw new ArgumentException("Catalog and item ids must be alphanumeric, at most 32 characters.");
        return container.GetBlobClient($"{catalogId}/{itemId}");
    }

    private static bool IsSafeId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 32 && id.All(char.IsAsciiLetterOrDigit);
}
