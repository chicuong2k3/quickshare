using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuickShare.Data;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStorageProvider, LocalDiskStorageProviderStub>();
            RemoveHostedService<DeliveryCleanupService>(services);
            RemoveHostedService<ZombiePartSweeper>(services);
            RemoveHostedService<DeliveryExpiryJob>(services);
            RemoveHostedService<SubscriptionExpiryJob>(services);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        return CreateClient();
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (descriptor is not null)
            services.Remove(descriptor);
    }
}

internal sealed class LocalDiskStorageProviderStub : IStorageProvider
{
    public Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default)
        => Task.FromResult($"stub://{chunkId}");

    public Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());

    public Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
        => Task.FromResult(new R2InitResult("stub-upload-id", $"stub/{deliveryId}/{fileName}"));

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2PartUrl>>(partNumbers
            .Select(n => new R2PartUrl(n, new Uri("http://stub.local/")))
            .ToList());

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => Task.FromResult(new CompletionResult(true, null));

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri("http://stub.local/download"));

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => Task.CompletedTask;
}
