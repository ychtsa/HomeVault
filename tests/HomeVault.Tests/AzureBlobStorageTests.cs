using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HomeVault.Services;
using Moq;

namespace HomeVault.Tests;

public class AzureBlobStorageTests
{
    [Theory]
    [InlineData("../other", "abc12")]
    [InlineData("abc12", "a/b")]
    [InlineData("", "abc12")]
    public async Task InvalidIdsNeverReachStorage(string catalogId, string itemId)
    {
        var container = new Mock<BlobContainerClient>(MockBehavior.Strict);
        var storage = new AzureBlobCatalogImageStorage(container.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(catalogId, itemId));
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadUsesCatalogPartitionAndTreatsMissingBlobAsAbsent()
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Blob not found"));
        var container = new Mock<BlobContainerClient>(MockBehavior.Strict);
        container.Setup(c => c.GetBlobClient("cat12/item1")).Returns(blob.Object);

        var storage = new AzureBlobCatalogImageStorage(container.Object);
        Assert.Null(await storage.OpenReadAsync("cat12", "item1"));
        container.Verify(c => c.GetBlobClient("cat12/item1"), Times.Once);
    }

    [Fact]
    public async Task StoragePermissionFailuresAreNotSilentlyReportedAsMissingImages()
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));
        var container = new Mock<BlobContainerClient>();
        container.Setup(c => c.GetBlobClient("cat12/item1")).Returns(blob.Object);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            new AzureBlobCatalogImageStorage(container.Object).OpenReadAsync("cat12", "item1"));
        Assert.Equal(403, exception.Status);
    }
}
