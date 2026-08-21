using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Data residency (issue #119): pinning a delivery's bytes to a storage location.
///
/// Routing is derived from the object key rather than passed through <see cref="IStorageProvider"/>
/// on every call, so the key format *is* the contract — these cases pin it. The important
/// property is backward compatibility: keys written before residency existed have a user GUID
/// where the region segment now sits, and must keep resolving to the default bucket. A 32-char
/// hex string can never match a region code, and that is what makes the scheme safe rather than
/// merely convenient.
/// </summary>
public sealed class StorageResidencyTests
{
    private const string EuBucket = "quickshare-eu";
    private const string UsBucket = "quickshare-us";

    // ── Key format ──

    [Fact]
    public void Key_WithoutARegionKeepsItsOriginalShape()
    {
        var user = Guid.NewGuid();
        var delivery = Guid.NewGuid();

        var key = R2ObjectKey.For(user, delivery, "report.pdf");

        Assert.Equal($"uploads/{user:N}/{delivery:N}/report.pdf", key);
        Assert.Null(StorageRegions.FromKey(key));
    }

    [Fact]
    public void Key_WithARegionCarriesItDirectlyUnderTheUploadsPrefix()
    {
        var key = R2ObjectKey.For(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "eu");

        // Still one namespace, so the orphan sweep is unaffected.
        Assert.StartsWith(R2ObjectKey.UploadsPrefix, key, StringComparison.Ordinal);
        Assert.Equal("eu", StorageRegions.FromKey(key));
    }

    [Fact]
    public void Key_IgnoresARegionCodeItWouldNotAccept()
    {
        // Anything that could be confused with a GUID segment, or carry a path, is not a code.
        var key = R2ObjectKey.For(Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "EU/../secret");

        Assert.Null(StorageRegions.FromKey(key));
    }

    [Fact]
    public void Key_AUserGuidIsNeverMistakenForARegion()
    {
        // The whole backward-compatibility argument in one assertion: every pre-residency key
        // has this shape, and must resolve to the default bucket.
        var key = $"uploads/{Guid.NewGuid():N}/{Guid.NewGuid():N}/file.bin";

        Assert.Null(StorageRegions.FromKey(key));
    }

    [Fact]
    public void Key_NonUploadNamespacesAreNeverRegionRouted()
    {
        // Avatars and brand assets are small non-content objects that stay in the default
        // bucket; routing them would scatter a workspace logo across jurisdictions.
        Assert.Null(StorageRegions.FromKey("avatars/eu/whatever.png"));
        Assert.Null(StorageRegions.FromKey("brand/eu/logo"));
        Assert.Null(StorageRegions.FromKey(null));
        Assert.Null(StorageRegions.FromKey(string.Empty));
    }

    [Theory]
    [InlineData("eu", true)]
    [InlineData("apac2", true)]
    [InlineData("EU", false)]          // uppercase could collide with hex-ish segments
    [InlineData("e", false)]           // too short to be meaningful
    [InlineData("eu-west-1", false)]   // hyphens would complicate key parsing
    [InlineData("", false)]
    public void RegionCode_IsRestrictedToShortLowercaseAlphanumerics(string code, bool valid)
    {
        Assert.Equal(valid, StorageRegions.IsValidCode(code));
    }

    // ── Configuration ──

    [Fact]
    public void Options_IgnoreHalfConfiguredRegions()
    {
        var options = new ObjectStoreOptions
        {
            Endpoint = "https://minio.internal:9000",
            BucketName = "default",
            AccessKeyId = "key",
            AccessKeySecret = "secret",
            Regions =
            [
                new StorageRegionOption { Code = "eu", Label = "EU", BucketName = EuBucket },
                new StorageRegionOption { Code = "us", Label = "US" },            // no bucket
                new StorageRegionOption { Code = "BAD", BucketName = UsBucket },  // invalid code
            ],
        };

        var usable = options.UsableRegions;

        Assert.True(options.SupportsResidency);
        Assert.Single(usable);
        Assert.Equal("eu", usable[0].Code);
        Assert.Null(options.FindRegion("us"));
        Assert.Null(options.FindRegion(null));
    }

    [Fact]
    public void Options_CarryRegionsEvenWhenConnectionDetailsComeFromTheLegacySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["R2:AccountId"] = "legacy-account",
                ["R2:BucketName"] = "legacy-bucket",
                ["R2:AccessKeyId"] = "legacy-key",
                ["R2:AccessKeySecret"] = "legacy-secret",
                ["ObjectStore:Regions:0:Code"] = "eu",
                ["ObjectStore:Regions:0:Label"] = "European Union",
                ["ObjectStore:Regions:0:BucketName"] = EuBucket,
            })
            .Build();

        var resolved = ObjectStoreResolver.Resolve(configuration);

        // Regions are a separate concern from which section holds the credentials. Dropping
        // them on fallback silently disabled residency with no error anywhere — exactly the
        // failure the fallback exists to prevent.
        Assert.Equal("https://legacy-account.r2.cloudflarestorage.com", resolved.ResolvedEndpoint);
        Assert.True(resolved.SupportsResidency);
        Assert.Equal(EuBucket, resolved.FindRegion("eu")!.BucketName);
    }

    [Fact]
    public void Options_WithoutRegionsDoNotOfferResidency()
    {
        var options = new ObjectStoreOptions { BucketName = "default" };

        // Every deployment configured before this feature lands here, and keeps working.
        Assert.False(options.SupportsResidency);
        Assert.Empty(options.UsableRegions);
    }

    // ── Entitlement ──

    [Fact]
    public void Resolver_GivesNothingWhenTheDeploymentHasNoRegions()
    {
        var resolver = Resolver(regions: []);
        var studio = new User { Plan = PlanTierLimits.Studio };

        // Offering a control that does nothing is worse than not offering one.
        Assert.False(resolver.SupportsResidency);
        Assert.Null(resolver.Resolve(studio, PlanTierLimits.Studio, "eu"));
    }

    [Fact]
    public void Resolver_StudioRoutesPerDelivery()
    {
        var resolver = Resolver();
        var studio = new User { Plan = PlanTierLimits.Studio, PreferredStorageRegion = "eu" };

        // The per-request choice wins: an agency holds clients under different regimes.
        Assert.Equal("us", resolver.Resolve(studio, PlanTierLimits.Studio, "us"));
        // Falling back to the account preference when nothing is requested.
        Assert.Equal("eu", resolver.Resolve(studio, PlanTierLimits.Studio, null));
    }

    [Fact]
    public void Resolver_ProPinsOneRegionAndIgnoresAPerRequestOverride()
    {
        var resolver = Resolver();
        var pro = new User { Plan = PlanTierLimits.Pro, PreferredStorageRegion = "eu" };

        // Honouring the override would quietly hand Pro the Studio feature.
        Assert.Equal("eu", resolver.Resolve(pro, PlanTierLimits.Pro, "us"));
        Assert.Equal("eu", resolver.Resolve(pro, PlanTierLimits.Pro, null));
    }

    [Fact]
    public void Resolver_FreeAndAnonymousGetTheDefaultBucket()
    {
        var resolver = Resolver();
        var free = new User { Plan = PlanTierLimits.Free, PreferredStorageRegion = "eu" };

        Assert.Null(resolver.Resolve(free, PlanTierLimits.Free, "eu"));
        Assert.Null(resolver.Resolve(null, PlanTierLimits.Anonymous, "eu"));
    }

    [Fact]
    public void Resolver_DegradesRatherThanFailingOnAnUnknownRegion()
    {
        var resolver = Resolver();
        var studio = new User { Plan = PlanTierLimits.Studio };

        // Residency is a deployment capability. An operator who removed a region should see
        // uploads keep working against the default bucket, not a validation failure.
        Assert.Null(resolver.Resolve(studio, PlanTierLimits.Studio, "atlantis"));
    }

    [Fact]
    public void Resolver_LabelsOnlyRegionsThatStillExist()
    {
        var resolver = Resolver();

        Assert.Equal("European Union", resolver.LabelFor("eu"));
        // A label we cannot resolve is worse than no badge at all.
        Assert.Null(resolver.LabelFor("atlantis"));
        Assert.Null(resolver.LabelFor(null));
    }

    // ── Helpers ──

    private static StorageRegionResolver Resolver(List<StorageRegionOption>? regions = null)
    {
        regions ??=
        [
            new StorageRegionOption { Code = "eu", Label = "European Union", BucketName = EuBucket },
            new StorageRegionOption { Code = "us", Label = "United States", BucketName = UsBucket },
        ];

        return new StorageRegionResolver(Options.Create(new ObjectStoreOptions
        {
            Endpoint = "https://minio.internal:9000",
            BucketName = "default",
            AccessKeyId = "key",
            AccessKeySecret = "secret",
            Regions = regions,
        }));
    }
}
