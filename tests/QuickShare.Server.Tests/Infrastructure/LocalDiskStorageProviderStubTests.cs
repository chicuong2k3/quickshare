using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure;
using QuickShare.Services;

namespace QuickShare.Tests.Infrastructure;

public sealed class LocalDiskStorageProviderStubTests
{
    private static readonly LocalDiskStorageProvider Provider = new(
        Options.Create(new StorageOptions
        {
            LocalDisk = new LocalDiskOptions { RootPath = "./test-storage" }
        }),
        NullLogger<LocalDiskStorageProvider>.Instance);

    [Fact]
    public async Task InitR2MultipartUploadAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.InitR2MultipartUploadAsync(Guid.NewGuid(), "test.txt"));
    }

    [Fact]
    public async Task GenerateUploadPartUrlsAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.GenerateUploadPartUrlsAsync("upload-id", "obj-key", [1, 2, 3]));
    }

    [Fact]
    public async Task CompleteR2MultipartUploadAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.CompleteR2MultipartUploadAsync("upload-id", "obj-key", []));
    }

    [Fact]
    public async Task AbortR2MultipartUploadAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.AbortR2MultipartUploadAsync("upload-id", "obj-key"));
    }

    [Fact]
    public async Task GetPresignedDownloadUrlAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.GetPresignedDownloadUrlAsync("obj-key", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task DeleteObjectsAsync_throws_NotSupportedException()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Provider.DeleteObjectsAsync("prefix/"));
    }

    [Fact]
    public void StoreChunkAsync_is_marked_obsolete()
    {
        var method = typeof(LocalDiskStorageProvider).GetMethod(nameof(LocalDiskStorageProvider.StoreChunkAsync));
        Assert.NotNull(method);
        var attr = method!.GetCustomAttributes(typeof(ObsoleteAttribute), false);
        Assert.NotEmpty(attr);
    }
}
