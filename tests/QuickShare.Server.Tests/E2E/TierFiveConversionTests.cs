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
                    Status = DeliveryStatus.Active,
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

    [Fact]
    public async Task BrevoEmailWebhook_SuppressesHardBouncesAndRepeatedSoftBounces()
    {
        var client = _factory.CreateClient();
        var hardEmail = $"hard-{Guid.NewGuid():N}@example.com";
        var softEmail = $"soft-{Guid.NewGuid():N}@example.com";

        var hardRes = await client.PostAsJsonAsync(RoutePaths.BrevoEmailWebhook, new { @event = "hard_bounce", email = hardEmail });
        Assert.Equal(HttpStatusCode.OK, hardRes.StatusCode);

        var sender = _factory.Services.GetRequiredService<QuickShare.Infrastructure.Email.EmailSender>();
        Assert.True(await sender.IsSuppressedAsync(hardEmail));

        await client.PostAsJsonAsync(RoutePaths.BrevoEmailWebhook, new { @event = "soft_bounce", email = softEmail });
        Assert.False(await sender.IsSuppressedAsync(softEmail));
        await client.PostAsJsonAsync(RoutePaths.BrevoEmailWebhook, new { @event = "soft_bounce", email = softEmail });
        Assert.False(await sender.IsSuppressedAsync(softEmail));
        await client.PostAsJsonAsync(RoutePaths.BrevoEmailWebhook, new { @event = "soft_bounce", email = softEmail });
        Assert.True(await sender.IsSuppressedAsync(softEmail));
    }

    [Fact]
    public async Task DownloadPage_RendersSubtitlesAndImageCompareSlider_AndConvertsSrtToVtt()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var shareCode = Guid.NewGuid().ToString("N")[..10];
        var deliveryId = Guid.CreateVersion7();
        var subFileId = Guid.CreateVersion7();
        var imgBeforeId = Guid.CreateVersion7();
        var imgAfterId = Guid.CreateVersion7();
        var subObjectKey = $"sub/{subFileId:N}.srt";

        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Storage.IStorageProvider>();
            var srtBytes = System.Text.Encoding.UTF8.GetBytes("1\r\n00:00:01,250 --> 00:00:04,500\r\nXin chao QuickShare\r\n");
            await using var ms = new MemoryStream(srtBytes);
            await storage.UploadObjectAsync(subObjectKey, ms, "text/plain");

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = deliveryId,
                SenderId = user.Id,
                FileName = "demo.mp4",
                ContentType = "video/mp4",
                FileSize = 10240,
                TotalBytes = 10240,
                ShareCode = shareCode,
                StoragePath = "stub/demo.mp4",
                MaxDownloads = 10,
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(3),
            });
            db.DeliveryFiles.AddRange(
                new DeliveryFile
                {
                    Id = subFileId,
                    DeliveryId = deliveryId,
                    FileName = "demo.vi.srt",
                    ContentType = "text/plain",
                    FileSize = srtBytes.Length,
                    ObjectKey = subObjectKey,
                    SortOrder = 1,
                },
                new DeliveryFile
                {
                    Id = imgBeforeId,
                    DeliveryId = deliveryId,
                    FileName = "before.png",
                    ContentType = "image/png",
                    FileSize = 2048,
                    ObjectKey = "stub/before.png",
                    SortOrder = 2,
                },
                new DeliveryFile
                {
                    Id = imgAfterId,
                    DeliveryId = deliveryId,
                    FileName = "after.png",
                    ContentType = "image/png",
                    FileSize = 2048,
                    ObjectKey = "stub/after.png",
                    SortOrder = 3,
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var vttRes = await client.GetAsync($"/api/downloads/{shareCode}/subtitles/{subFileId}");
        Assert.Equal(HttpStatusCode.OK, vttRes.StatusCode);
        Assert.Contains("text/vtt", vttRes.Content.Headers.ContentType?.ToString() ?? string.Empty);
        var vttBody = await vttRes.Content.ReadAsStringAsync();
        Assert.StartsWith("WEBVTT", vttBody);
        Assert.Contains("00:00:01.250 --> 00:00:04.500", vttBody);
        Assert.Contains("Xin chao QuickShare", vttBody);

        var pageHtml = await client.GetStringAsync($"{RoutePaths.DeliveryPage}{shareCode}");
        Assert.Contains("id=\"dl-subtitles\"", pageHtml);
        Assert.Contains("id=\"dl-subtitle-track-select\"", pageHtml);
        Assert.Contains("id=\"dl-image-compare\"", pageHtml);
        Assert.Contains("id=\"dl-compare-slider\"", pageHtml);
    }

    [Fact]
    public async Task P2PPairingAndQrScanner_MintsAndConsumesOneTimeSixDigitKey()
    {
        var client = _factory.CreateClient();
        var createRes = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "pair-test.zip",
            FileSize = 1024L,
            MimeType = "application/zip",
            BaseUrl = "https://localhost"
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var created = await createRes.Content.ReadFromJsonAsync<QuickShare.Features.Shares.P2P.CreateP2PShareResponse>();
        Assert.NotNull(created);
        Assert.Equal(6, created!.PairingCode.Length);
        Assert.All(created.PairingCode, c => Assert.True(char.IsAsciiDigit(c)));

        var resolveRes = await client.GetAsync($"{RoutePaths.P2pPairRoute}/{created.PairingCode}");
        Assert.Equal(HttpStatusCode.OK, resolveRes.StatusCode);
        var resolved = await resolveRes.Content.ReadFromJsonAsync<QuickShare.Features.Shares.P2P.ResolveP2PPairingResponse>();
        Assert.Equal(created.ShareCode, resolved!.ShareCode);

        var secondRes = await client.GetAsync($"{RoutePaths.P2pPairRoute}/{created.PairingCode}");
        Assert.Equal(HttpStatusCode.NotFound, secondRes.StatusCode);

        var p2pHtml = await client.GetStringAsync(RoutePaths.P2pSend);
        Assert.Contains("id=\"p2ps-pair-card\"", p2pHtml);
        Assert.Contains("id=\"p2ps-qr-scan-card\"", p2pHtml);
    }

    [Fact]
    public async Task BrandedEmails_AppliesWorkspaceSenderIdentityReplyToAndHtmlChrome()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Studio);
        var workspaceId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Studio Nam",
                OwnerId = user.Id,
                Seats = 5,
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = user.Id,
                InviteEmail = user.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);
        var putRes = await client.PutAsJsonAsync("/api/workspace/brand", new
        {
            enabled = true,
            accentColor = "#0f172a",
            accentTextColor = "#f8fafc",
            headerText = "Delivered by Studio Nam",
            footerText = "Studio Nam Co., Ltd — District 1, HCMC",
            hideQuickShareBranding = true,
            emailSenderName = "Studio Nam Delivery",
            emailReplyTo = "hello@studionam.vn",
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var brandDto = await putRes.Content.ReadFromJsonAsync<QuickShare.Features.Workspaces.Branding.BrandDto>();
        Assert.NotNull(brandDto);
        Assert.Equal("Studio Nam Delivery", brandDto!.EmailSenderName);
        Assert.Equal("hello@studionam.vn", brandDto.EmailReplyTo);

        using (var scope = _factory.Services.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Workspaces.Branding.BrandReader>();
            var view = await reader.ForWorkspaceAsync(workspaceId, CancellationToken.None);
            Assert.NotNull(view);

            var sender = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Email.EmailSender>();
            var identity = QuickShare.Infrastructure.Email.EmailBrandIdentity.FromBrand(view, "https://quickshare.test");
            var msg = sender.BuildMessage("client@acme.vn", "Client", "Contract ready", "<p>Download link</p>", identity);

            Assert.Equal("Studio Nam Delivery", msg.From.Mailboxes.Single().Name);
            Assert.Equal("hello@studionam.vn", msg.ReplyTo.Mailboxes.Single().Address);
            Assert.Contains("Delivered by Studio Nam", msg.HtmlBody);
            Assert.Contains("Studio Nam Co., Ltd", msg.HtmlBody);
            Assert.DoesNotContain("Powered by QuickShareCloud", msg.HtmlBody);
        }
    }

    [Fact]
    public async Task SeasonalCampaigns_CreateAndDisplayActiveBanner()
    {
        var adminClient = _factory.CreateAuthenticatedClient();
        var campRes = await adminClient.PostAsJsonAsync(RoutePaths.AdminCampaignsApi, new
        {
            template = "tet",
            code = "TET2026",
            campaignName = "Tết Nguyên Đán 2026",
            value = 30,
            durationDays = 14,
            autoApplyOnCheckout = true,
        });
        Assert.Equal(HttpStatusCode.OK, campRes.StatusCode);

        var anonClient = _factory.CreateClient();
        var activeRes = await anonClient.GetFromJsonAsync<QuickShare.Features.Payments.Promos.ActiveCampaignDto>(RoutePaths.ActiveCampaignsApi);
        Assert.NotNull(activeRes);
        Assert.Equal("TET2026", activeRes!.Code);

        var pricingHtml = await anonClient.GetStringAsync(RoutePaths.Pricing);
        Assert.Contains("id=\"pricing-campaign-banner\"", pricingHtml);
        Assert.Contains("TET2026", pricingHtml);
    }

    [Fact]
    public async Task CohortAndFunnelAnalytics_ComputesWeeklyRetentionAndExportsCsv()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var day0 = DateTime.UtcNow.Date.AddDays(-14);
        var day8 = day0.AddDays(8);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AnalyticsEvents.AddRange(
                new AnalyticsEvent
                {
                    Id = Guid.CreateVersion7(),
                    EventName = QuickShare.Features.Analytics.AnalyticsEvents.Visitor,
                    VisitorId = visitorA,
                    Plan = PlanTierLimits.Pro,
                },
                new AnalyticsEvent
                {
                    Id = Guid.CreateVersion7(),
                    EventName = QuickShare.Features.Analytics.AnalyticsEvents.UploadCompleted,
                    VisitorId = visitorA,
                    Plan = PlanTierLimits.Pro,
                },
                new AnalyticsEvent
                {
                    Id = Guid.CreateVersion7(),
                    EventName = QuickShare.Features.Analytics.AnalyticsEvents.Visitor,
                    VisitorId = visitorB,
                    Plan = PlanTierLimits.Pro,
                });
            await db.SaveChangesAsync();

            // Backdate visitorA's first event and visitorB's event to day0, and visitorA's second event to day8 (+1 week)
            var rows = db.AnalyticsEvents.Where(e => e.VisitorId == visitorA || e.VisitorId == visitorB).ToList();
            db.Entry(rows[0]).Property(x => x.CreatedAt).CurrentValue = day0;
            db.Entry(rows[1]).Property(x => x.CreatedAt).CurrentValue = day8;
            db.Entry(rows[2]).Property(x => x.CreatedAt).CurrentValue = day0;
            await db.SaveChangesAsync();
        }

        var adminClient = _factory.CreateAuthenticatedClient();
        var data = await adminClient.GetFromJsonAsync<QuickShare.Features.Analytics.Dashboard.AnalyticsDashboardResponse>(
            $"{QuickShare.Features.Analytics.AnalyticsConstants.AdminAnalyticsRoute}?plan=pro");
        Assert.NotNull(data);
        Assert.NotNull(data!.Cohorts);
        Assert.NotEmpty(data.Cohorts!);
        Assert.Contains(data.Cohorts!, c => c.CohortSize >= 2 && c.RetainedWeek1 >= 1);

        var csvRes = await adminClient.GetAsync(RoutePaths.AdminAnalyticsCsvRoute);
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        var csv = await csvRes.Content.ReadAsStringAsync();
        Assert.Contains("funnel,steps,", csv);
        Assert.Contains("cohort,", csv);

        var pageHtml = await adminClient.GetStringAsync(RoutePaths.AdminAnalytics);
        Assert.Contains("id=\"admin-analytics-cohorts\"", pageHtml);
        Assert.Contains("id=\"admin-analytics-export-csv\"", pageHtml);
    }

    // -----------------------------------------------------------------
    // #140 — Failed payment recovery: dunning emails + 7-day grace period
    // -----------------------------------------------------------------

    [Fact]
    public async Task FailedPaymentDunning_EntersGracePeriod_KeepsPaidFeatures_AndDowngradesAfterGrace()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var subId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Subscriptions.Add(new Subscription
            {
                Id = subId,
                UserId = user.Id,
                Plan = PlanTierLimits.Pro,
                Status = SubscriptionStatus.Active,
                BillingPeriod = BillingPeriod.Monthly,
                CurrentPeriodStart = now.AddDays(-31),
                CurrentPeriodEnd = now.AddMinutes(-5),
            });
            await db.SaveChangesAsync();

            var expiredCount = await QuickShare.Features.Payments.Subscriptions.SubscriptionService.ExpireStaleAsync(db, CancellationToken.None);
            Assert.Equal(0, expiredCount);

            var sub = db.Subscriptions.First(s => s.Id == subId);
            var u = db.Users.First(x => x.Id == user.Id);
            Assert.Equal(1, sub.DunningAttemptCount);
            Assert.NotNull(sub.DunningGraceEndsAt);
            Assert.True(sub.IsInDunningGrace);
            Assert.Equal(PlanTierLimits.Pro, u.Plan);
        }

        var client = _factory.CreateAuthenticatedClient(key);
        var billingHtml = await client.GetStringAsync(RoutePaths.AccountBilling);
        Assert.Contains("id=\"billing-dunning-banner\"", billingHtml);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = db.Subscriptions.First(s => s.Id == subId);
            sub.DunningGraceEndsAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();

            var expiredCount = await QuickShare.Features.Payments.Subscriptions.SubscriptionService.ExpireStaleAsync(db, CancellationToken.None);
            Assert.True(expiredCount >= 1);

            var updatedSub = db.Subscriptions.First(s => s.Id == subId);
            var updatedUser = db.Users.First(x => x.Id == user.Id);
            Assert.Equal(SubscriptionStatus.Expired, updatedSub.Status);
            Assert.Equal(PlanTierLimits.Free, updatedUser.Plan);
        }
    }

    // -----------------------------------------------------------------
    // #102 — Gift Pro subscription: buy for a friend + claim flow
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #120 — Multi-currency pricing & checkout (USD / EUR / VND)
    // -----------------------------------------------------------------

    [Fact]
    public async Task MultiCurrencyPricing_DetectsRegionAndFormatsUsdEurVnd()
    {
        Assert.Equal(PricingKeys.DefaultCurrency, QuickShare.Features.Payments.PlanCatalog.DetectCurrency(null, "VN", "en-US"));
        Assert.Equal(PricingKeys.CurrencyEur, QuickShare.Features.Payments.PlanCatalog.DetectCurrency(null, "DE", "de-DE"));
        Assert.Equal(PricingKeys.CurrencyUsd, QuickShare.Features.Payments.PlanCatalog.DetectCurrency(null, "US", "en-US"));
        Assert.Equal(PricingKeys.CurrencyEur, QuickShare.Features.Payments.PlanCatalog.DetectCurrency("eur", "VN", "vi-VN"));

        var client = _factory.CreateClient();
        var usdHtml = await client.GetStringAsync($"{RoutePaths.Pricing}?currency=USD");
        Assert.Contains("id=\"pricing-currency-selector\"", usdHtml);
        Assert.Contains("$9", usdHtml);

        var eurHtml = await client.GetStringAsync($"{RoutePaths.Pricing}?currency=EUR");
        Assert.Contains("8", WebUtility.HtmlDecode(eurHtml));

        var vndHtml = await client.GetStringAsync($"{RoutePaths.Pricing}?currency=VND");
        Assert.Contains("50", WebUtility.HtmlDecode(vndHtml));
    }

    // -----------------------------------------------------------------
    // #199 — API versioning strategy: /api/v1/* + X-API-Version + Sunset
    // -----------------------------------------------------------------

    [Fact]
    public async Task ApiVersioning_RoutesV1Endpoints_AndEmitsVersionAndSunsetHeaders()
    {
        var client = _factory.CreateAuthenticatedClient();

        var v1Res = await client.GetAsync("/api/v1/gifts");
        Assert.Equal(HttpStatusCode.OK, v1Res.StatusCode);
        Assert.True(v1Res.Headers.TryGetValues(QuickShare.Infrastructure.Middleware.ApiVersioningMiddleware.VersionHeader, out var versions));
        Assert.Contains(QuickShare.Infrastructure.Middleware.ApiVersioningMiddleware.CurrentVersion, versions!);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/gifts");
        req.Headers.Add(QuickShare.Infrastructure.Middleware.ApiVersioningMiddleware.VersionHeader, "v0");
        var depRes = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, depRes.StatusCode);
        Assert.True(depRes.Headers.Contains(QuickShare.Infrastructure.Middleware.ApiVersioningMiddleware.DeprecationHeader));
        Assert.True(depRes.Headers.Contains(QuickShare.Infrastructure.Middleware.ApiVersioningMiddleware.SunsetHeader));

        var docsHtml = await client.GetStringAsync(RoutePaths.ApiDocs);
        Assert.Contains("id=\"apidocs-version\"", docsHtml);
        Assert.Contains("/api/v1/deliveries", docsHtml);
    }

    // -----------------------------------------------------------------
    // #307 — Full-text search: filename, tag, message, OCR content + privacy gate
    // -----------------------------------------------------------------

    [Fact]
    public async Task FullTextSearch_FindsByFileNameTagMessageAndContent_AndExcludesPrivateModeContent()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var publicDeliveryId = Guid.CreateVersion7();
        var privateDeliveryId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var folderId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                OwnerId = user.Id,
                Name = "Search Workspace",
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = user.Id,
                InviteEmail = user.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            db.WorkspaceFolders.Add(new WorkspaceFolder
            {
                Id = folderId,
                WorkspaceId = workspaceId,
                Name = "Quarterly Tax Audit",
                CreatedById = user.Id,
            });
            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = publicDeliveryId,
                    ShareCode = $"srchpub{user.Id.ToString("N")[..5]}",
                    SenderId = user.Id,
                    WorkspaceId = workspaceId,
                    FolderId = folderId,
                    FileName = "invoice-2026.pdf",
                    ContentType = "application/pdf",
                    FileSize = 2048,
                    TotalBytes = 2048,
                    StoragePath = "stub/search-pub",
                    Message = "Contains nebula-quantum topology notes",
                    PrivacyMode = false,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                },
                new Delivery
                {
                    Id = privateDeliveryId,
                    ShareCode = $"srchprv{user.Id.ToString("N")[..5]}",
                    SenderId = user.Id,
                    WorkspaceId = workspaceId,
                    FileName = "secret-vault.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    TotalBytes = 1024,
                    StoragePath = "stub/search-prv",
                    Message = "classified-omegasignal passphrase inside",
                    PrivacyMode = true,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);

        var ocrRes = await client.GetFromJsonAsync<QuickShare.Features.Search.GlobalSearchResponse>(
            $"{RoutePaths.SearchApi}?q=Hyperion");
        Assert.NotNull(ocrRes);
        Assert.Single(ocrRes!.Files);
        Assert.Equal("content", ocrRes.Files[0].MatchSource);
        Assert.Contains("Hyperion", ocrRes.Files[0].Snippet);

        var folderRes = await client.GetFromJsonAsync<QuickShare.Features.Search.GlobalSearchResponse>(
            $"{RoutePaths.SearchApi}?q=Quarterly");
        Assert.NotNull(folderRes);
        Assert.Single(folderRes!.Folders);
        Assert.Single(folderRes.Files);

        var privRes = await client.GetFromJsonAsync<QuickShare.Features.Search.GlobalSearchResponse>(
            $"{RoutePaths.SearchApi}?q=omegasignal");
        Assert.NotNull(privRes);
        Assert.Empty(privRes!.Files);
    }

    // -----------------------------------------------------------------
    // #92 — A/B testing framework: deterministic assignment, Z-score & winner promotion
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #85 — Pay-per-transfer: one-time credits without subscription (anonymous-friendly)
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #298 — Affiliate program: unique link, click tracking & 20% commission on first paid plan
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #272 — Migration wizard: dry-run preview, conflict resolution (skip/rename/replace) & legal-hold guard
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #41 — Download speed queue (Smash boarding model)
    // -----------------------------------------------------------------

    [Fact]
    public async Task DownloadSpeedQueue_QueuesLargeFreeTransfers_SuppressesAdsWhenQueued_AndBypassesForPaidPlans()
    {
        var freeKey = Guid.CreateVersion7().ToString();
        var freeUser = await _factory.SeedUserAsync(freeKey, PlanTierLimits.Free);
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);

        var freeLargeCode = $"qfree{freeUser.Id.ToString("N")[..5]}";
        var proLargeCode = $"qpro{proUser.Id.ToString("N")[..6]}";
        var largeSize = 300L * 1024 * 1024; // 300 MB >= 200 MB threshold

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = freeUser.Id,
                    ShareCode = freeLargeCode,
                    FileName = "indie-film-master.mp4",
                    ContentType = "video/mp4",
                    FileSize = largeSize,
                    TotalBytes = largeSize,
                    StoragePath = "stub/qfree-large",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = proUser.Id,
                    ShareCode = proLargeCode,
                    FileName = "studio-grade-export.mp4",
                    ContentType = "video/mp4",
                    FileSize = largeSize,
                    TotalBytes = largeSize,
                    StoragePath = "stub/qpro-large",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                });
            await db.SaveChangesAsync();
        }

        var anonClient = _factory.CreateClient();

        // 1. Free large transfer enters standard boarding queue and suppresses ad interstitial
        using (var scope = _factory.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
            var freeRes = await bus.Send(new QuickShare.Features.Downloads.Info.DownloadInfoQuery(freeLargeCode));
            Assert.True(freeRes.IsSuccess(out var freeInfo));
            Assert.True(freeInfo!.IsQueued);
            Assert.True(freeInfo.QueuePosition >= 1);
            Assert.True(freeInfo.QueueEtaSeconds >= 5);
            Assert.True(freeInfo.SuppressAdInterstitial);

            var proRes = await bus.Send(new QuickShare.Features.Downloads.Info.DownloadInfoQuery(proLargeCode));
            Assert.True(proRes.IsSuccess(out var proInfo));
            Assert.False(proInfo!.IsQueued);
            Assert.Equal(0, proInfo.QueuePosition);
        }

        var freePage = await anonClient.GetStringAsync($"/d/{freeLargeCode}");
        Assert.Contains("id=\"dl-queue-banner\"", freePage);
    }

    // -----------------------------------------------------------------
    // #54 — Upload queue for large free files
    // -----------------------------------------------------------------

    [Fact]
    public async Task UploadQueue_QueuesLargeFreeUploads_AndBypassesForPaidUsers()
    {
        var freeKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(freeKey, PlanTierLimits.Free);
        var proKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);

        var largeSize = 250L * 1024 * 1024; // 250 MB >= 200 MB threshold

        var freeClient = _factory.CreateAuthenticatedClient(freeKey);
        var freeStart = await freeClient.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            fileName = "large-free-archive.zip",
            fileSize = largeSize,
            contentType = "application/zip",
            partNumbers = new[] { 1, 2 },
        });
        Assert.Equal(HttpStatusCode.OK, freeStart.StatusCode);
        var freeDto = await freeStart.Content.ReadFromJsonAsync<QuickShare.Features.Uploads.StartR2.StartR2Response>();
        Assert.NotNull(freeDto);
        Assert.True(freeDto!.IsQueued);
        Assert.True(freeDto.QueuePosition >= 1);
        Assert.True(freeDto.QueueEtaSeconds >= 5);

        var proClient = _factory.CreateAuthenticatedClient(proKey);
        var proStart = await proClient.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            fileName = "large-pro-archive.zip",
            fileSize = largeSize,
            contentType = "application/zip",
            partNumbers = new[] { 1, 2 },
        });
        Assert.Equal(HttpStatusCode.OK, proStart.StatusCode);
        var proDto = await proStart.Content.ReadFromJsonAsync<QuickShare.Features.Uploads.StartR2.StartR2Response>();
        Assert.NotNull(proDto);
        Assert.False(proDto!.IsQueued);
        Assert.Equal(0, proDto.QueuePosition);
    }

    // -----------------------------------------------------------------
    // #197 — Chunk/file-level deduplication: tenant-scoped reuse, privacy bypass & ref-counted GC
    // -----------------------------------------------------------------

    [Fact]
    public async Task Deduplication_ReusesIdenticalBlobWithinSameScope_IsolatesTenants_AndRefCountsCleanup()
    {
        var userAKey = Guid.CreateVersion7().ToString();
        var userA = await _factory.SeedUserAsync(userAKey, PlanTierLimits.Pro);
        var userBKey = Guid.CreateVersion7().ToString();
        var userB = await _factory.SeedUserAsync(userBKey, PlanTierLimits.Pro);

        var sha = "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e";
        var d1Id = Guid.CreateVersion7();
        var d2Id = Guid.CreateVersion7();
        var dOtherTenantId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r2 = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Storage.IStorageProvider>();
            var scopeA = QuickShare.Features.Uploads.Shared.DeduplicationService.BuildScopeKey(null, userA.Id, false);
            var scopeB = QuickShare.Features.Uploads.Shared.DeduplicationService.BuildScopeKey(null, userB.Id, false);

            var d1 = new Delivery
            {
                Id = d1Id,
                SenderId = userA.Id,
                ShareCode = $"dedup1{userA.Id.ToString("N")[..4]}",
                FileName = "design-system-v1.fig",
                ContentType = "application/octet-stream",
                FileSize = 10 * 1024 * 1024,
                TotalBytes = 10 * 1024 * 1024,
                StoragePath = $"uploads/{userA.Id:N}/{d1Id:N}/design-system-v1.fig",
                Sha256 = sha,
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            };
            var d2 = new Delivery
            {
                Id = d2Id,
                SenderId = userA.Id,
                ShareCode = $"dedup2{userA.Id.ToString("N")[..4]}",
                FileName = "design-system-copy.fig",
                ContentType = "application/octet-stream",
                FileSize = 10 * 1024 * 1024,
                TotalBytes = 10 * 1024 * 1024,
                StoragePath = $"uploads/{userA.Id:N}/{d2Id:N}/design-system-copy.fig",
                Sha256 = sha,
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            };
            var dOther = new Delivery
            {
                Id = dOtherTenantId,
                SenderId = userB.Id,
                ShareCode = $"dedupb{userB.Id.ToString("N")[..4]}",
                FileName = "other-tenant.fig",
                ContentType = "application/octet-stream",
                FileSize = 10 * 1024 * 1024,
                TotalBytes = 10 * 1024 * 1024,
                StoragePath = $"uploads/{userB.Id:N}/{dOtherTenantId:N}/other-tenant.fig",
                Sha256 = sha,
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            };

            db.Deliveries.AddRange(d1, d2, dOther);
            await db.SaveChangesAsync();

            // 1st upload registers canonical blob (RefCount = 1, IsDeduplicated = false)
            var out1 = await QuickShare.Features.Uploads.Shared.DeduplicationService.RegisterOrReuseAsync(
                db, r2, scopeA, d1.Sha256, d1.FileSize, d1.StoragePath, CancellationToken.None);
            Assert.False(out1.ReusedExisting);
            await db.SaveChangesAsync();

            // 2nd upload by same user with same SHA-256 reuses canonical StoragePath (RefCount = 2, IsDeduplicated = true)
            var out2 = await QuickShare.Features.Uploads.Shared.DeduplicationService.RegisterOrReuseAsync(
                db, r2, scopeA, d2.Sha256, d2.FileSize, d2.StoragePath, CancellationToken.None);
            Assert.True(out2.ReusedExisting);
            d2.StoragePath = out2.CanonicalStoragePath;
            d2.IsDeduplicated = true;
            Assert.Equal(d1.StoragePath, d2.StoragePath);

            // Cross-tenant upload by userB with identical SHA-256 NEVER shares userA's blob (#190 isolation)
            var outOther = await QuickShare.Features.Uploads.Shared.DeduplicationService.RegisterOrReuseAsync(
                db, r2, scopeB, dOther.Sha256, dOther.FileSize, dOther.StoragePath, CancellationToken.None);
            Assert.False(outOther.ReusedExisting);
            Assert.NotEqual(d1.StoragePath, outOther.CanonicalStoragePath);

            await db.SaveChangesAsync();

            var blob = db.DeduplicatedBlobs.First(b => b.StoragePath == d1.StoragePath);
            Assert.Equal(2, blob.RefCount);

            // Releasing 1st reference decrements RefCount to 1 without deleting underlying R2 object
            await QuickShare.Features.Uploads.Shared.DeduplicationService.ReleaseOrDeleteAsync(db, r2, d1.StoragePath, true, CancellationToken.None);
            await db.SaveChangesAsync();
            var blobAfterOne = db.DeduplicatedBlobs.FirstOrDefault(b => b.StoragePath == d1.StoragePath);
            Assert.NotNull(blobAfterOne);
            Assert.Equal(1, blobAfterOne!.RefCount);
        }

        var clientA = _factory.CreateAuthenticatedClient(userAKey);
        var usageHtml = await clientA.GetStringAsync(RoutePaths.AccountUsage);
        Assert.Contains("id=\"usage-dedup-bytes\"", usageHtml);
    }

    // -----------------------------------------------------------------
    // #186 — Zapier + Make integration: catalog, scoped token + webhook connection & revocation
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #118 — Passkey recovery codes: generate 10 single-use codes, spend on login, require re-add
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #313 — Encryption key management: workspace policy, PBKDF2 passphrase & key escrow
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #109 — Cloud import: stream from Google Drive / Dropbox / OneDrive without storing credentials
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #279, #26 — PWA offline delivery bundle & privacy-mode guard
    // -----------------------------------------------------------------

    [Fact]
    public async Task PwaOfflineReadAndQueue_ServesOfflineBundle_AndBlocksPrivacyMode()
    {
        var senderKey = Guid.CreateVersion7().ToString();
        var sender = await _factory.SeedUserAsync(senderKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(senderKey);

        var normalCode = $"off{sender.Id.ToString("N")[..7]}";
        var privateCode = $"prv{sender.Id.ToString("N")[..7]}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = sender.Id,
                    ShareCode = normalCode,
                    FileName = "field-manual.pdf",
                    ContentType = "application/pdf",
                    FileSize = 8192,
                    TotalBytes = 8192,
                    StoragePath = $"uploads/{sender.Id:N}/{normalCode}/field-manual.pdf",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    PrivacyMode = false,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = sender.Id,
                    ShareCode = privateCode,
                    FileName = "classified-notes.txt",
                    ContentType = "text/plain",
                    FileSize = 1024,
                    TotalBytes = 1024,
                    StoragePath = $"uploads/{sender.Id:N}/{privateCode}/classified-notes.txt",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    PrivacyMode = true,
                    ExpiresAt = DateTime.UtcNow.AddDays(3),
                });
            await db.SaveChangesAsync();
        }

        // 1. Normal delivery returns offline bundle and renders #dl-save-offline-btn
        var bundleRes = await client.GetAsync($"/api/downloads/{normalCode}/offline-bundle");
        Assert.Equal(HttpStatusCode.OK, bundleRes.StatusCode);
        var bundle = await bundleRes.Content.ReadFromJsonAsync<QuickShare.Features.Downloads.Offline.OfflineDeliveryBundleDto>();
        Assert.NotNull(bundle);
        Assert.Equal(normalCode, bundle!.ShareCode);
        Assert.True(bundle.MaxCacheQuotaBytes > 0);

        var normalHtml = await client.GetStringAsync($"/d/{normalCode}");
        Assert.Contains("id=\"dl-save-offline-btn\"", normalHtml);

        // 2. PrivacyMode delivery refuses offline caching with 403 and hides button
        var privBundleRes = await client.GetAsync($"/api/downloads/{privateCode}/offline-bundle");
        Assert.Equal(HttpStatusCode.Forbidden, privBundleRes.StatusCode);

        var privHtml = await client.GetStringAsync($"/d/{privateCode}");
        Assert.DoesNotContain("id=\"dl-save-offline-btn\"", privHtml);

    }

    // -----------------------------------------------------------------
    // #136 — Video compression presets at upload
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #32 — P2P / Wormhole-style instant live streaming before upload completes
    // -----------------------------------------------------------------

    [Fact]
    public async Task InstantLiveStreaming_MintsShareCodeAtStartR2_ShowsLiveReceivingProgress_AndPreservesCodeAtCommit()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(key);

        // 1. Start multipart upload -> ShareCode and ShareLink are minted immediately before commit
        var startRes = await client.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            fileName = "live-broadcast.mp4",
            fileSize = 20L * 1024 * 1024,
            contentType = "video/mp4",
            partNumbers = new[] { 1, 2, 3, 4 },
        });
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
        var started = await startRes.Content.ReadFromJsonAsync<QuickShare.Features.Uploads.StartR2.StartR2Response>();
        Assert.NotNull(started);
        Assert.False(string.IsNullOrWhiteSpace(started!.ShareCode));
        Assert.Contains($"/d/{started.ShareCode}", started.ShareLink);

        // 2. Sender reports 2 of 4 parts uploaded (50%)
        var progRes = await client.PostAsJsonAsync(RoutePaths.LiveStreamProgressApi, new
        {
            deliveryId = started.DeliveryId,
            uploadId = started.UploadId,
            uploadedPartsCount = 2,
            uploadedBytes = 10L * 1024 * 1024,
        });
        Assert.Equal(HttpStatusCode.OK, progRes.StatusCode);

        // 3. Recipient visits /d/{shareCode} while upload is still in progress -> sees #dl-live-streaming-banner with 50%
        var liveHtml = await client.GetStringAsync($"/d/{started.ShareCode}");
        Assert.Contains("id=\"dl-live-streaming-banner\"", liveHtml);
        Assert.Contains("50%", liveHtml);
    }

    // -----------------------------------------------------------------
    // #196 — SFTP ingest into workspace folders
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #198 — GitHub release-asset sharing
    // -----------------------------------------------------------------


    // -----------------------------------------------------------------
    // #131, #70, #46, #71, #200 — SDKs, CLI, Self-Hosted Docker, Telegram Bot & Discord Bot
    // -----------------------------------------------------------------


}
