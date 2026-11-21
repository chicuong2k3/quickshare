using Microsoft.Extensions.Configuration;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// S3-compatible object storage (#171).
///
/// The storage cases pin *configuration resolution* rather than round-tripping bytes: the
/// provider is the AWS SDK either way, and what actually changed in #171 is which endpoint,
/// bucket and addressing style it is handed — plus the promise that an existing deployment
/// configured under the old `R2` section keeps working after the upgrade. Contract tests
/// against a live MinIO belong in CI with a container, not in an in-memory suite.
/// </summary>
public sealed class ObjectStoreConfigurationTests : IClassFixture<TestWebAppFactory>
{
    private const string MinioEndpoint = "https://minio.internal:9000";
    private const string Bucket = "quickshare-objects";

    // ── Object store configuration (#171) ──

    [Fact]
    public void ObjectStore_DerivesTheR2EndpointWhenOnlyAnAccountIdIsGiven()
    {
        var options = new ObjectStoreOptions { AccountId = "abc123" };

        Assert.Equal("https://abc123.r2.cloudflarestorage.com", options.ResolvedEndpoint);
    }

    [Fact]
    public void ObjectStore_PrefersAnExplicitEndpointOverTheDerivedOne()
    {
        var options = new ObjectStoreOptions { AccountId = "abc123", Endpoint = MinioEndpoint };

        // Explicit config wins, which is the whole of #171: a self-hoster points at MinIO.
        Assert.Equal(MinioEndpoint, options.ResolvedEndpoint);
    }

    [Fact]
    public void ObjectStore_IsNotConfiguredWithoutCredentialsOrBucket()
    {
        Assert.False(new ObjectStoreOptions().IsConfigured);
        Assert.False(new ObjectStoreOptions { Endpoint = MinioEndpoint }.IsConfigured);
        Assert.False(new ObjectStoreOptions
        {
            Endpoint = MinioEndpoint,
            AccessKeyId = "key",
            AccessKeySecret = "secret",
        }.IsConfigured);

        Assert.True(new ObjectStoreOptions
        {
            Endpoint = MinioEndpoint,
            AccessKeyId = "key",
            AccessKeySecret = "secret",
            BucketName = Bucket,
        }.IsConfigured);
    }

    [Fact]
    public void ObjectStore_FallsBackToTheLegacyR2SectionSoUpgradesDoNotBreak()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["R2:AccountId"] = "legacy-account",
                ["R2:BucketName"] = "legacy-bucket",
                ["R2:AccessKeyId"] = "legacy-key",
                ["R2:AccessKeySecret"] = "legacy-secret",
                ["R2:PublicBaseUrl"] = "https://cdn.example.com",
            })
            .Build();

        var resolved = ObjectStoreResolver.Resolve(configuration);

        Assert.True(resolved.IsConfigured);
        Assert.Equal("https://legacy-account.r2.cloudflarestorage.com", resolved.ResolvedEndpoint);
        Assert.Equal("legacy-bucket", resolved.BucketName);
        Assert.Equal("https://cdn.example.com", resolved.PublicBaseUrl);
    }

    [Fact]
    public void ObjectStore_CurrentSectionWinsOverTheLegacyOne()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["R2:AccountId"] = "legacy-account",
                ["R2:BucketName"] = "legacy-bucket",
                ["R2:AccessKeyId"] = "legacy-key",
                ["R2:AccessKeySecret"] = "legacy-secret",
                ["ObjectStore:Endpoint"] = MinioEndpoint,
                ["ObjectStore:BucketName"] = "minio-bucket",
                ["ObjectStore:AccessKeyId"] = "minio-key",
                ["ObjectStore:AccessKeySecret"] = "minio-secret",
                ["ObjectStore:ForcePathStyle"] = "false",
            })
            .Build();

        var resolved = ObjectStoreResolver.Resolve(configuration);

        Assert.Equal(MinioEndpoint, resolved.ResolvedEndpoint);
        Assert.Equal("minio-bucket", resolved.BucketName);
        Assert.False(resolved.ForcePathStyle);
    }
}
