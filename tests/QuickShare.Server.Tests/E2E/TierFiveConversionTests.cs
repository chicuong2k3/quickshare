using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Downloads.Info;
using QuickShare.Features.Downloads.PublicStats;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Tier 5 conversion, trust &amp; developer features:
/// - #130 wider share sheet (X, LinkedIn, Threads, Telegram, Email + copy Markdown/HTML)
/// - #135 live social proof on landing page
/// - #169 pricing calculator on pricing page
/// - #207 changelog RSS 2.0 feed + alternate link discovery
/// - #160 verified sender badge on delivery page for Pro/Studio (suppressed on Free &amp; PrivacyMode)
/// - #267 download geolocation (CountryCode on DownloadTracking, PublicStats &amp; CSV export)
/// - #209 referral milestones &amp; monthly leaderboard on /account/referral
/// - #185/#281 interactive API playground &amp; SDK reference on /api-docs
/// </summary>
public sealed class TierFiveConversionTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public TierFiveConversionTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UploadPage_RendersWiderShareSheetTargets_AndCopyButtons()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Send);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"upload-share-targets\"", html);
        Assert.Contains("id=\"upload-copy-markdown-btn\"", html);
        Assert.Contains("id=\"upload-copy-html-btn\"", html);
        Assert.Contains("id=\"upload-share-x\"", html);
        Assert.Contains("id=\"upload-share-linkedin\"", html);
        Assert.Contains("id=\"upload-share-threads\"", html);
        Assert.Contains("id=\"upload-share-telegram\"", html);
        Assert.Contains("id=\"upload-share-email\"", html);
    }

    [Fact]
    public async Task HomePage_RendersLiveSocialProofSection()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Home);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"home-social-proof\"", html);
        Assert.Contains("id=\"home-proof-deliveries\"", html);
        Assert.Contains("id=\"home-proof-volume\"", html);
        Assert.Contains("id=\"home-proof-e2e\"", html);
    }

    [Fact]
    public async Task PricingPage_RendersInteractivePricingCalculator()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Pricing);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"pricing-calculator\"", html);
        Assert.Contains("id=\"calc-files\"", html);
        Assert.Contains("id=\"calc-size-mb\"", html);
        Assert.Contains("id=\"calc-retention-days\"", html);
        Assert.Contains("id=\"calc-volume-out\"", html);
        Assert.Contains("id=\"calc-plan-out\"", html);
    }

    [Fact]
    public async Task ChangelogRss_ReturnsValidRssXml_AndChangelogHasDiscoveryLink()
    {
        var client = _factory.CreateClient();

        var rssRes = await client.GetAsync(RoutePaths.ChangelogRss);
        Assert.Equal(HttpStatusCode.OK, rssRes.StatusCode);
        Assert.Contains("application/rss+xml", rssRes.Content.Headers.ContentType?.ToString() ?? string.Empty);

        var xml = await rssRes.Content.ReadAsStringAsync();
        Assert.Contains("<rss version=\"2.0\">", xml);
        Assert.Contains("<title>QuickShareCloud Changelog</title>", xml);
        Assert.Contains("<item>", xml);

        var pageRes = await client.GetAsync(RoutePaths.Changelog);
        Assert.Equal(HttpStatusCode.OK, pageRes.StatusCode);
        var html = WebUtility.HtmlDecode(await pageRes.Content.ReadAsStringAsync());
        Assert.Contains("type=\"application/rss+xml\"", html);
        Assert.Contains(RoutePaths.ChangelogRss, html);
    }

    [Fact]
    public async Task VerifiedSenderBadge_ShownForPro_HiddenForFreeAndPrivacyMode()
    {
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);

        var freeKey = Guid.CreateVersion7().ToString();
        var freeUser = await _factory.SeedUserAsync(freeKey, PlanTierLimits.Free);

        string proCode;
        string proPrivateCode;
        string freeCode;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            proCode = Guid.NewGuid().ToString("N")[..10];
            proPrivateCode = Guid.NewGuid().ToString("N")[..10];
            freeCode = Guid.NewGuid().ToString("N")[..10];

            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = proUser.Id,
                    FileName = "pro-verified.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    TotalBytes = 1024,
                    ShareCode = proCode,
                    StoragePath = "stub/pro",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                    PrivacyMode = false,
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = proUser.Id,
                    FileName = "pro-private.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    TotalBytes = 1024,
                    ShareCode = proPrivateCode,
                    StoragePath = "stub/pro-priv",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                    PrivacyMode = true,
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = freeUser.Id,
                    FileName = "free.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    TotalBytes = 1024,
                    ShareCode = freeCode,
                    StoragePath = "stub/free",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                    PrivacyMode = false,
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        var proPage = await client.GetStringAsync($"/d/{proCode}");
        Assert.Contains("id=\"dl-badge-verified\"", proPage);
        Assert.Contains("\"verifiedSender\":true", proPage);

        var privPage = await client.GetStringAsync($"/d/{proPrivateCode}");
        Assert.DoesNotContain("id=\"dl-badge-verified\"", privPage);
        Assert.Contains("\"verifiedSender\":false", privPage);

        var freePage = await client.GetStringAsync($"/d/{freeCode}");
        Assert.DoesNotContain("id=\"dl-badge-verified\"", freePage);
        Assert.Contains("\"verifiedSender\":false", freePage);
    }

    [Fact]
    public async Task DownloadGeolocation_AggregatesCountryInPublicStats_AndExportsCountryColumn()
    {
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);
        var shareCode = Guid.NewGuid().ToString("N")[..10];
        var deliveryId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = deliveryId,
                SenderId = proUser.Id,
                FileName = "geo-report.pdf",
                ContentType = "application/pdf",
                FileSize = 2048,
                TotalBytes = 2048,
                ShareCode = shareCode,
                StoragePath = "stub/geo",
                MaxDownloads = 50,
                Status = DeliveryStatus.Active,
                PublicStatsEnabled = true,
                ExpiresAt = DateTime.UtcNow.AddDays(5),
            });

            db.DownloadTrackings.AddRange(
                new DownloadTracking
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = deliveryId,
                    IpAddress = "203.0.113.10",
                    DownloadedAt = DateTime.UtcNow.AddMinutes(-10),
                    CountryCode = "VN",
                },
                new DownloadTracking
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = deliveryId,
                    IpAddress = "203.0.113.11",
                    DownloadedAt = DateTime.UtcNow.AddMinutes(-5),
                    CountryCode = "VN",
                },
                new DownloadTracking
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = deliveryId,
                    IpAddress = "198.51.100.7",
                    DownloadedAt = DateTime.UtcNow.AddMinutes(-2),
                    CountryCode = "US",
                });
            await db.SaveChangesAsync();
        }

        var anonClient = _factory.CreateClient();
        var stats = await anonClient.GetFromJsonAsync<PublicStatsResponse>($"/api/deliveries/{shareCode}/public-stats");
        Assert.NotNull(stats);
        Assert.NotNull(stats.Countries);
        Assert.Equal(2, stats.Countries.Count);
        Assert.Equal("VN", stats.Countries[0].CountryCode);
        Assert.Equal(2, stats.Countries[0].Count);
        Assert.Equal("US", stats.Countries[1].CountryCode);
        Assert.Equal(1, stats.Countries[1].Count);

        var statsHtml = await anonClient.GetStringAsync($"/d/{shareCode}/stats");
        Assert.Contains("id=\"ps-countries\"", statsHtml);
        Assert.Contains("VN", statsHtml);

        var proClient = _factory.CreateAuthenticatedClient(proKey);
        var csv = await proClient.GetStringAsync("/api/deliveries/export?days=30");
        Assert.Contains("ShareCode,FileName,DownloadedAt,IpHash,Country", csv);
        Assert.Contains("\"VN\"", csv);
        Assert.Contains("\"US\"", csv);
    }

    [Fact]
    public async Task ReferralPage_RendersMilestonesAndLeaderboard()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Free);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ReferralEvents.Add(new ReferralEvent
            {
                Id = Guid.CreateVersion7(),
                ReferrerUserId = user.Id,
                ReferredUserId = Guid.CreateVersion7(),
                Type = ReferralEventTypes.Rewarded,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);
        var html = await client.GetStringAsync(RoutePaths.AccountReferral);
        Assert.Contains("id=\"referral-milestones\"", html);
        Assert.Contains("id=\"referral-leaderboard\"", html);
    }

    [Fact]
    public async Task ApiDocsPage_RendersInteractivePlaygroundAndSdkSnippets()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.ApiDocs);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"api-playground\"", html);
        Assert.Contains("id=\"apidocs-api-key\"", html);
        Assert.Contains("id=\"apidocs-endpoint\"", html);
        Assert.Contains("id=\"apidocs-snippet-curl\"", html);
        Assert.Contains("id=\"apidocs-snippet-js\"", html);
        Assert.Contains("id=\"apidocs-snippet-py\"", html);
        Assert.Contains("id=\"api-reference\"", html);
    }

    [Fact]
    public async Task EmbeddableWidget_RendersDropZone_WithFrameableCsp()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var reqCode = Guid.NewGuid().ToString("N")[..10];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FileRequests.Add(new FileRequest
            {
                Id = Guid.CreateVersion7(),
                OwnerId = user.Id,
                Code = reqCode,
                Title = "Client Asset Drop",
                Kind = FileRequestKind.Request,
                MaxFiles = 20,
                MaxTotalBytes = 100 * 1024 * 1024,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var res = await client.GetAsync($"{RoutePaths.WidgetPage}{reqCode}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var csp = res.Headers.TryGetValues(HttpHeaders.ContentSecurityPolicy, out var vals)
            ? string.Join(";", vals)
            : string.Empty;
        Assert.Contains("frame-ancestors 'self' https: http:", csp);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"widget-dropzone\"", html);
        Assert.Contains("Client Asset Drop", html);
        Assert.Contains("id=\"widget-upload-btn\"", html);
    }

    [Fact]
    public async Task DeliveryCardEndpoint_RendersSvg_RespectsPendingApprovalAndPasswordGate()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var openCode = Guid.NewGuid().ToString("N")[..10];
        var gatedCode = Guid.NewGuid().ToString("N")[..10];
        var pendingCode = Guid.NewGuid().ToString("N")[..10];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = user.Id,
                    FileName = "brand-deck-2026.pdf",
                    ContentType = "application/pdf",
                    FileSize = 4096,
                    TotalBytes = 4096,
                    ShareCode = openCode,
                    StoragePath = "stub/open",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = user.Id,
                    FileName = "top-secret-acquisition.pdf",
                    ContentType = "application/pdf",
                    FileSize = 4096,
                    TotalBytes = 4096,
                    ShareCode = gatedCode,
                    StoragePath = "stub/gated",
                    PasswordHash = "hashed-pw",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = user.Id,
                    FileName = "pending-review.pdf",
                    ContentType = "application/pdf",
                    FileSize = 4096,
                    TotalBytes = 4096,
                    ShareCode = pendingCode,
                    StoragePath = "stub/pending",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.PendingApproval,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        var openRes = await client.GetAsync($"/api/downloads/{openCode}/card.svg");
        Assert.Equal(HttpStatusCode.OK, openRes.StatusCode);
        Assert.Contains("image/svg+xml", openRes.Content.Headers.ContentType?.ToString() ?? string.Empty);
        var openSvg = await openRes.Content.ReadAsStringAsync();
        Assert.Contains("brand-deck-2026.pdf", openSvg);

        var gatedSvg = await client.GetStringAsync($"/api/downloads/{gatedCode}/card.svg");
        Assert.DoesNotContain("top-secret-acquisition.pdf", gatedSvg);
        Assert.Contains("Protected delivery", gatedSvg);

        var pendingRes = await client.GetAsync($"/api/downloads/{pendingCode}/card.svg");
        Assert.Equal(HttpStatusCode.NotFound, pendingRes.StatusCode);
    }

    [Fact]
    public async Task UploadPage_RendersCameraCaptureAndVoiceNoteControls()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync(RoutePaths.Send);
        Assert.Contains("id=\"upload-camera-toggle\"", html);
        Assert.Contains("id=\"upload-camera-mode\"", html);
        Assert.Contains("id=\"upload-voice-toggle\"", html);
        Assert.Contains("id=\"upload-voice-mode\"", html);
        Assert.Contains("id=\"upload-voice-start-btn\"", html);
        Assert.Contains("id=\"upload-voice-stop-btn\"", html);
    }
}
