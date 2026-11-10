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
    public async Task SeasonalCampaignsAndStudentDiscounts_AutoVerifiesEduVnAndEnforcesNonShareableCode()
    {
        var studentKey = Guid.CreateVersion7().ToString();
        var student = await _factory.SeedUserAsync(studentKey, PlanTierLimits.Free);
        var otherKey = Guid.CreateVersion7().ToString();
        var otherUser = await _factory.SeedUserAsync(otherKey, PlanTierLimits.Free);

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
        Assert.Contains("id=\"pricing-education-discount\"", pricingHtml);

        var studentClient = _factory.CreateAuthenticatedClient(studentKey);
        var applyRes = await studentClient.PostAsJsonAsync(RoutePaths.AccountDiscountApi, new
        {
            kind = DiscountApplication.KindStudent,
            organizationName = "Hanoi University of Science and Technology",
            verificationEmail = "sv@hust.edu.vn",
        });
        Assert.Equal(HttpStatusCode.OK, applyRes.StatusCode);
        var appDto = await applyRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.Promos.DiscountApplicationDto>();
        Assert.NotNull(appDto);
        Assert.Equal(DiscountApplication.StatusApproved, appDto!.Status);
        Assert.NotNull(appDto.IssuedPromoCode);
        Assert.StartsWith("EDU-", appDto.IssuedPromoCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var promos = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Payments.Promos.PromoCodeService>();
            var studentQuote = await promos.QuoteAsync(appDto.IssuedPromoCode, student.Id, PlanTierLimits.Pro, 100, CancellationToken.None);
            Assert.True(studentQuote.Valid);
            Assert.Equal(50, studentQuote.FinalAmount);

            // Another user trying to use the student's personal code is refused.
            var otherQuote = await promos.QuoteAsync(appDto.IssuedPromoCode, otherUser.Id, PlanTierLimits.Pro, 100, CancellationToken.None);
            Assert.False(otherQuote.Valid);
            Assert.Equal(ErrorCodes.PromoCodeNotApplicable, otherQuote.Error);
        }
    }

    [Fact]
    public async Task WalletPass_GeneratesValidPkpassZip_AndMasksPrivateOrGatedDeliveries()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var openCode = Guid.NewGuid().ToString("N")[..10];
        var privateCode = Guid.NewGuid().ToString("N")[..10];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = user.Id,
                    FileName = "boarding-contract.pdf",
                    ContentType = "application/pdf",
                    FileSize = 8192,
                    TotalBytes = 8192,
                    ShareCode = openCode,
                    StoragePath = "stub/open-pass",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(5),
                },
                new Delivery
                {
                    Id = Guid.CreateVersion7(),
                    SenderId = user.Id,
                    FileName = "confidential-payroll.xlsx",
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    FileSize = 8192,
                    TotalBytes = 8192,
                    ShareCode = privateCode,
                    StoragePath = "stub/private-pass",
                    PrivacyMode = true,
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(5),
                });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var openRes = await client.GetAsync($"/api/downloads/{openCode}/wallet-pass");
        Assert.Equal(HttpStatusCode.OK, openRes.StatusCode);
        Assert.Equal("application/vnd.apple.pkpass", openRes.Content.Headers.ContentType?.MediaType);

        var zipBytes = await openRes.Content.ReadAsByteArrayAsync();
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes), System.IO.Compression.ZipArchiveMode.Read))
        {
            var passEntry = archive.GetEntry("pass.json");
            var manifestEntry = archive.GetEntry("manifest.json");
            Assert.NotNull(passEntry);
            Assert.NotNull(manifestEntry);

            using var reader = new StreamReader(passEntry!.Open());
            var passJson = await reader.ReadToEndAsync();
            Assert.Contains("boarding-contract.pdf", passJson);
            Assert.Contains("PKBarcodeFormatQR", passJson);
        }

        var privRes = await client.GetAsync($"/api/downloads/{privateCode}/wallet-pass");
        Assert.Equal(HttpStatusCode.OK, privRes.StatusCode);
        var privBytes = await privRes.Content.ReadAsByteArrayAsync();
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(privBytes), System.IO.Compression.ZipArchiveMode.Read))
        {
            using var reader = new StreamReader(archive.GetEntry("pass.json")!.Open());
            var passJson = await reader.ReadToEndAsync();
            Assert.DoesNotContain("confidential-payroll.xlsx", passJson);
            Assert.Contains("Protected Delivery", passJson);
        }
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

    [Fact]
    public async Task GiftSubscription_RefusesSelfGift_ActivatesOnPayment_AndClaimsForFriend()
    {
        var senderKey = Guid.CreateVersion7().ToString();
        var sender = await _factory.SeedUserAsync(senderKey, PlanTierLimits.Pro);
        var friendKey = Guid.CreateVersion7().ToString();
        var friend = await _factory.SeedUserAsync(friendKey, PlanTierLimits.Free);
        var friendEmail = $"friend-{friend.Id:N}@quickshare.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var f = db.Users.First(u => u.Id == friend.Id);
            f.Email = friendEmail;
            await db.SaveChangesAsync();
        }

        var senderClient = _factory.CreateAuthenticatedClient(senderKey);

        var selfRes = await senderClient.PostAsJsonAsync(RoutePaths.GiftsApi, new
        {
            recipientEmail = sender.Email,
            plan = PlanTierLimits.Pro,
            billing = BillingPeriods.Monthly,
            message = "For myself",
        });
        Assert.Equal(HttpStatusCode.BadRequest, selfRes.StatusCode);

        var createRes = await senderClient.PostAsJsonAsync(RoutePaths.GiftsApi, new
        {
            recipientEmail = friendEmail,
            plan = PlanTierLimits.Pro,
            billing = BillingPeriods.Monthly,
            message = "Happy shipping!",
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.Gifts.GiftSubscriptionDto>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Token));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var intent = db.PaymentIntents.First(i => i.Id == created.PaymentIntentId);
            var checkout = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Payments.CheckoutService>();
            var completed = await checkout.CompleteAsync(intent, "GIFT-TX-102", CancellationToken.None);
            Assert.True(completed);
        }

        var anonClient = _factory.CreateClient();
        var preview = await anonClient.GetFromJsonAsync<QuickShare.Features.Payments.Gifts.GiftSubscriptionDto>(
            $"{RoutePaths.GiftsApi}/{created.Token}");
        Assert.NotNull(preview);
        Assert.Equal(SubscriptionGift.StatusClaimable, preview!.Status);

        var selfClaim = await senderClient.PostAsync($"{RoutePaths.GiftsApi}/{created.Token}/claim", null);
        Assert.Equal(HttpStatusCode.BadRequest, selfClaim.StatusCode);

        var friendClient = _factory.CreateAuthenticatedClient(friendKey);
        var claimRes = await friendClient.PostAsync($"{RoutePaths.GiftsApi}/{created.Token}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var friendUser = db.Users.First(u => u.Id == friend.Id);
            var giftRow = db.SubscriptionGifts.First(g => g.Id == created.Id);
            Assert.Equal(PlanTierLimits.Pro, friendUser.Plan);
            Assert.Equal(SubscriptionGift.StatusClaimed, giftRow.Status);
            Assert.Equal(friend.Id, giftRow.ClaimedByUserId);
        }

        var dupClaim = await friendClient.PostAsync($"{RoutePaths.GiftsApi}/{created.Token}/claim", null);
        Assert.Equal(HttpStatusCode.Conflict, dupClaim.StatusCode);
    }

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
            db.DeliveryOcrTexts.AddRange(
                new DeliveryOcrText
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = publicDeliveryId,
                    Text = "Payment due to Acme Hyperion Holdings by Friday.",
                },
                new DeliveryOcrText
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = privateDeliveryId,
                    Text = "classified-omegasignal top secret body text",
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

    [Fact]
    public async Task AbTestingFramework_AssignsDeterministically_ComputesSignificance_AndPromotesWinner()
    {
        var adminClient = _factory.CreateAuthenticatedClient();

        var createRes = await adminClient.PostAsJsonAsync(RoutePaths.AdminExperimentsApi, new
        {
            key = "landing_hero_headline",
            name = "Landing Hero Copy Test",
            surface = "landing",
            variantACopy = "Send large files privately",
            variantBCopy = "Zero-knowledge file sharing in seconds",
            trafficSplitPercent = 50,
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        // Deterministic assignment for the same visitor id
        var vId = Guid.NewGuid();
        var v1 = QuickShare.Features.Experiments.ExperimentEvaluator.AssignVariant(vId, "landing_hero_headline", 50);
        var v2 = QuickShare.Features.Experiments.ExperimentEvaluator.AssignVariant(vId, "landing_hero_headline", 50);
        Assert.Equal(v1, v2);

        // Seed 500 exposures for A (25 conversions = 5%) and 500 exposures for B (60 conversions = 12%)
        var (zScore, confidence, recWinner) = QuickShare.Features.Experiments.ExperimentEvaluator.ComputeSignificance(500, 25, 500, 60);
        Assert.True(zScore > 2.5);
        Assert.Equal(99.0, confidence);
        Assert.Equal(Experiment.VariantB, recWinner);

        // Promote Variant B to 100% traffic
        var promoteRes = await adminClient.PostAsJsonAsync($"{RoutePaths.AdminExperimentsApi}/landing_hero_headline/promote", new
        {
            winnerVariant = Experiment.VariantB,
        });
        Assert.Equal(HttpStatusCode.OK, promoteRes.StatusCode);

        var anonClient = _factory.CreateClient();
        var assignDto = await anonClient.GetFromJsonAsync<QuickShare.Features.Experiments.ExperimentAssignmentDto>(
            $"{RoutePaths.ExperimentsAssignApi}?key=landing_hero_headline");
        Assert.NotNull(assignDto);
        Assert.Equal(Experiment.VariantB, assignDto!.Variant);
        Assert.Equal("Zero-knowledge file sharing in seconds", assignDto.Copy);
        Assert.Equal(Experiment.StatusPromoted, assignDto.Status);
    }

    // -----------------------------------------------------------------
    // #85 — Pay-per-transfer: one-time credits without subscription (anonymous-friendly)
    // -----------------------------------------------------------------

    [Fact]
    public async Task PayPerTransferPass_AllowsAnonymousLargeFileUploadWithoutSubscription()
    {
        var anonClient = _factory.CreateClient();
        var threeGb = 3L * 1024 * 1024 * 1024; // 3 GB > 2 GB Free/Anonymous cap

        // 1. Without a pass, anonymous 3 GB upload is rejected at start-r2
        var blockedRes = await anonClient.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            fileName = "raw-footage-4k.mov",
            fileSize = threeGb,
            contentType = "video/quicktime",
            partNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, blockedRes.StatusCode);

        // 2. Anonymous user purchases a one-time transfer pass
        var buyRes = await anonClient.PostAsJsonAsync(RoutePaths.TransferCreditsApi, new
        {
            email = "anon-filmmaker@example.com",
            quantity = 1,
            currency = PricingKeys.DefaultCurrency,
        });
        Assert.Equal(HttpStatusCode.OK, buyRes.StatusCode);
        var passDto = await buyRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.Credits.TransferPassDto>();
        Assert.NotNull(passDto);
        Assert.False(string.IsNullOrEmpty(passDto!.Token));

        // 3. Activate pass after PayOS payment
        var actRes = await anonClient.PostAsync($"{RoutePaths.TransferCreditsApi}/{passDto.Token}/activate", null);
        Assert.Equal(HttpStatusCode.OK, actRes.StatusCode);

        // 4. Anonymous user starts 3 GB upload with the activated transferPassToken -> succeeds!
        var startRes = await anonClient.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            fileName = "raw-footage-4k.mov",
            fileSize = threeGb,
            contentType = "video/quicktime",
            partNumbers = new[] { 1 },
            transferPassToken = passDto.Token,
        });
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
    }

    // -----------------------------------------------------------------
    // #298 — Affiliate program: unique link, click tracking & 20% commission on first paid plan
    // -----------------------------------------------------------------

    [Fact]
    public async Task AffiliateProgram_EnrollsTracksClicks_AndCreditsCommissionOnFirstPaidConversion()
    {
        var affKey = Guid.CreateVersion7().ToString();
        var affUser = await _factory.SeedUserAsync(affKey, PlanTierLimits.Pro);
        var buyerKey = Guid.CreateVersion7().ToString();
        var buyerUser = await _factory.SeedUserAsync(buyerKey, PlanTierLimits.Free);

        var affClient = _factory.CreateAuthenticatedClient(affKey);
        var enrollRes = await affClient.PostAsJsonAsync(RoutePaths.AffiliateApi, new
        {
            customCode = $"creator-{affUser.Id.ToString("N")[^6..]}",
            payoutMethod = "bank_transfer",
            payoutDetails = "VCB 0011009999999",
            taxCode = "0101234567",
            termsAccepted = true,
        });
        Assert.Equal(HttpStatusCode.OK, enrollRes.StatusCode);
        var enrolled = await enrollRes.Content.ReadFromJsonAsync<QuickShare.Features.Affiliates.AffiliateDashboardDto>();
        Assert.NotNull(enrolled);

        // Anonymous prospect clicks affiliate link
        var anonClient = _factory.CreateClient();
        var clickRes = await anonClient.PostAsync($"{RoutePaths.AffiliateApi}/{enrolled!.Code}/click", null);
        Assert.Equal(HttpStatusCode.OK, clickRes.StatusCode);

        // Link buyer to affiliate referrer and complete a paid Pro checkout (50,000 VND)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var b = db.Users.First(u => u.Id == buyerUser.Id);
            b.ReferredByUserId = affUser.Id;
            await db.SaveChangesAsync();

            var checkout = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Payments.CheckoutService>();
            var (intent, _) = await checkout.CreatePaymentLinkAsync(b, PlanTierLimits.Pro, false, CancellationToken.None);
            var completed = await checkout.CompleteAsync(intent, "AFF-INV-298", CancellationToken.None);
            Assert.True(completed);
        }

        var dash = await affClient.GetFromJsonAsync<QuickShare.Features.Affiliates.AffiliateDashboardDto>(RoutePaths.AffiliateApi);
        Assert.NotNull(dash);
        Assert.Equal(1, dash!.ClicksCount);
        Assert.Equal(1, dash.PaidConversionsCount);
        Assert.True(dash.TotalEarnedVnd > 0);
        Assert.Single(dash.RecentConversions);

        var portalHtml = await affClient.GetStringAsync(RoutePaths.AccountAffiliate);
        Assert.Contains("id=\"affiliate-portal-card\"", portalHtml);
        Assert.Contains(enrolled.Code, portalHtml);
    }

    // -----------------------------------------------------------------
    // #269 — SMS share: +84 normalization, 160-char budget, metered billing & STOP opt-out
    // -----------------------------------------------------------------

    [Fact]
    public async Task SmsShare_NormalizesVnPhone_Enforces160CharLimit_MetersInvoice_AndHonorsStopOptOut()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var shareCode = $"sms{user.Id.ToString("N")[..7]}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = user.Id,
                ShareCode = shareCode,
                FileName = "contract-signed.pdf",
                ContentType = "application/pdf",
                FileSize = 4096,
                TotalBytes = 4096,
                StoragePath = "stub/sms-test",
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(3),
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);

        // Send SMS to local VN number 0901234567 -> normalized to +84901234567
        var smsRes = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/sms", new
        {
            recipientPhone = "0901234567",
            passwordHint = "company tax code",
            recipientConsentConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.OK, smsRes.StatusCode);
        var smsDto = await smsRes.Content.ReadFromJsonAsync<QuickShare.Features.Deliveries.Sms.SmsDeliveryResponse>();
        Assert.NotNull(smsDto);
        Assert.Equal("+84901234567", smsDto!.RecipientPhone);
        Assert.True(smsDto.CharacterCount <= SmsDeliveryLog.MessageMaxLength);
        Assert.Contains(shareCode, smsDto.MessageBody);

        // Recipient replies STOP -> future SMS to same phone is refused with 409 Conflict
        var anonClient = _factory.CreateClient();
        var stopRes = await anonClient.PostAsJsonAsync(RoutePaths.SmsOptOutApi, new
        {
            phoneNumber = "0901234567",
            keyword = "STOP",
        });
        Assert.Equal(HttpStatusCode.OK, stopRes.StatusCode);

        var blockedSms = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/sms", new
        {
            recipientPhone = "+84901234567",
            recipientConsentConfirmed = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, blockedSms.StatusCode);
    }

    // -----------------------------------------------------------------
    // #272 — Migration wizard: dry-run preview, conflict resolution (skip/rename/replace) & legal-hold guard
    // -----------------------------------------------------------------

    [Fact]
    public async Task MigrationWizard_SupportsDryRun_ResolvesSameNameConflicts_AndRespectsLegalHold()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = user.Id,
                ShareCode = $"mig{user.Id.ToString("N")[..7]}",
                FileName = "brand-guidelines.pdf",
                ContentType = "application/pdf",
                FileSize = 1024,
                TotalBytes = 1024,
                StoragePath = "stub/existing-brand",
                Status = DeliveryStatus.Active,
                LegalHoldAt = DateTime.UtcNow,
                LegalHoldReason = "Litigation hold #266",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);

        // 1. ConflictPolicy = rename creates "brand-guidelines (imported).pdf" alongside existing file
        var renameRes = await client.PostAsJsonAsync(RoutePaths.MigrationImportApi, new
        {
            sourceProvider = MigrationImportJob.SourceWeTransfer,
            conflictPolicy = MigrationImportJob.ConflictRename,
            dryRun = false,
            items = new[]
            {
                new { fileName = "brand-guidelines.pdf", url = "https://example.com/brand-guidelines.pdf", estimatedBytes = 2048 },
                new { fileName = "campaign-deck.pptx", url = "https://example.com/campaign-deck.pptx", estimatedBytes = 4096 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, renameRes.StatusCode);
        var renameDto = await renameRes.Content.ReadFromJsonAsync<QuickShare.Features.Migration.MigrationWizardResponse>();
        Assert.NotNull(renameDto);
        Assert.Equal(2, renameDto!.ImportedCount);
        Assert.Contains(renameDto.Results, r => r.ResolvedFileName == "brand-guidelines (imported).pdf" && r.Action == "renamed");

        // 2. ConflictPolicy = replace skips legal-hold delivery (#266 compliance invariant!)
        var replaceRes = await client.PostAsJsonAsync(RoutePaths.MigrationImportApi, new
        {
            sourceProvider = MigrationImportJob.SourceGoogleDrive,
            conflictPolicy = MigrationImportJob.ConflictReplace,
            dryRun = false,
            items = new[]
            {
                new { fileName = "brand-guidelines.pdf", url = "https://example.com/brand-guidelines.pdf", estimatedBytes = 2048 },
            },
        });
        Assert.Equal(HttpStatusCode.OK, replaceRes.StatusCode);
        var replaceDto = await replaceRes.Content.ReadFromJsonAsync<QuickShare.Features.Migration.MigrationWizardResponse>();
        Assert.NotNull(replaceDto);
        Assert.Equal(1, replaceDto!.SkippedCount);
        Assert.Equal("skipped_legal_hold", replaceDto.Results[0].Action);

        var pageHtml = await client.GetStringAsync(RoutePaths.AccountMigration);
        Assert.Contains("id=\"migration-wizard-card\"", pageHtml);
    }

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
    // #48 — Virus scan for Pro/Studio: ClamAV scan badge & infected quarantine block
    // -----------------------------------------------------------------

    [Fact]
    public async Task VirusScan_MarksCleanForSafeFiles_AndBlocksInfectedFilesWithQuarantineNotice()
    {
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);
        var cleanId = Guid.CreateVersion7();
        var infectedId = Guid.CreateVersion7();
        var cleanCode = $"vclean{proUser.Id.ToString("N")[..4]}";
        var infectedCode = $"vinf{proUser.Id.ToString("N")[..6]}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Storage.IStorageProvider>();
            await storage.UploadObjectAsync(
                "stub/virus-eicar",
                new MemoryStream(System.Text.Encoding.ASCII.GetBytes(QuickShare.Features.Deliveries.VirusScan.ClamAvVirusScanner.EicarMarker)),
                "application/octet-stream",
                CancellationToken.None);

            db.Deliveries.AddRange(
                new Delivery
                {
                    Id = cleanId,
                    SenderId = proUser.Id,
                    ShareCode = cleanCode,
                    FileName = "quarterly-report.pdf",
                    ContentType = "application/pdf",
                    FileSize = 4096,
                    TotalBytes = 4096,
                    StoragePath = "stub/virus-clean",
                    Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    VirusScanStatus = DeliveryVirusScanStatus.Pending,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                },
                new Delivery
                {
                    Id = infectedId,
                    SenderId = proUser.Id,
                    ShareCode = infectedCode,
                    FileName = "eicar-test-sample.com",
                    ContentType = "application/octet-stream",
                    FileSize = 68,
                    TotalBytes = 68,
                    StoragePath = "stub/virus-eicar",
                    Sha256 = "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f",
                    MaxDownloads = 10,
                    Status = DeliveryStatus.Active,
                    VirusScanStatus = DeliveryVirusScanStatus.Pending,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                });
            await db.SaveChangesAsync();

            var job = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Deliveries.VirusScan.VirusScanJob>();
            await job.RunAsync();
        }

        var anonClient = _factory.CreateClient();

        // 1. Clean file renders #dl-badge-virus-clean on recipient page
        var cleanPage = await anonClient.GetStringAsync($"/d/{cleanCode}");
        Assert.Contains("id=\"dl-badge-virus-clean\"", cleanPage);

        // 2. Infected file is blocked on /download (410 Gone), and renders #download-phase-infected
        var infDlRes = await anonClient.GetAsync($"/api/downloads/{infectedCode}");
        Assert.Equal(HttpStatusCode.Gone, infDlRes.StatusCode);

        var infPage = await anonClient.GetStringAsync($"/d/{infectedCode}");
        Assert.Contains("id=\"download-phase-infected\"", infPage);
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
                db, r2, scopeA, d1.Sha256, d1.FileSize, d1.StorageRegion, d1.StoragePath, CancellationToken.None);
            Assert.False(out1.ReusedExisting);
            await db.SaveChangesAsync();

            // 2nd upload by same user with same SHA-256 reuses canonical StoragePath (RefCount = 2, IsDeduplicated = true)
            var out2 = await QuickShare.Features.Uploads.Shared.DeduplicationService.RegisterOrReuseAsync(
                db, r2, scopeA, d2.Sha256, d2.FileSize, d2.StorageRegion, d2.StoragePath, CancellationToken.None);
            Assert.True(out2.ReusedExisting);
            d2.StoragePath = out2.CanonicalStoragePath;
            d2.IsDeduplicated = true;
            Assert.Equal(d1.StoragePath, d2.StoragePath);

            // Cross-tenant upload by userB with identical SHA-256 NEVER shares userA's blob (#190 isolation)
            var outOther = await QuickShare.Features.Uploads.Shared.DeduplicationService.RegisterOrReuseAsync(
                db, r2, scopeB, dOther.Sha256, dOther.FileSize, dOther.StorageRegion, dOther.StoragePath, CancellationToken.None);
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
    // #314 — Cold storage tier: auto-transition, live quota relief & restore flow
    // -----------------------------------------------------------------

    [Fact]
    public async Task ColdStorageTier_ArchivesInactiveDeliveries_RelievesLiveQuota_AndRestoresToHotTier()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var deliveryId = Guid.CreateVersion7();
        var shareCode = $"cold{user.Id.ToString("N")[..6]}";
        var fiveGb = 5L * 1024 * 1024 * 1024;
        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var client = _factory.CreateAuthenticatedClient(key);

        // 1. Configure account cold storage threshold to 7 days
        var putPolicy = await client.PutAsJsonAsync(RoutePaths.AccountColdStorageApi, new
        {
            coldStorageDays = 7,
        });
        Assert.Equal(HttpStatusCode.OK, putPolicy.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserUsages.Add(new UserUsage
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                PeriodStart = periodStart,
                TotalBytesUploaded = fiveGb,
                TransferCount = 1,
            });
            var d = new Delivery
            {
                Id = deliveryId,
                SenderId = user.Id,
                ShareCode = shareCode,
                FileName = "raw-camera-cards.tar",
                ContentType = "application/x-tar",
                FileSize = fiveGb,
                TotalBytes = fiveGb,
                StoragePath = "stub/cold-archive-tar",
                MaxDownloads = 10,
                Status = DeliveryStatus.Active,
                StorageTier = DeliveryStorageTier.Hot,
                ExpiresAt = DateTime.UtcNow.AddDays(60),
            };
            db.Deliveries.Add(d);
            await db.SaveChangesAsync();

            // Backdate CreatedAt to 10 days ago (> 7-day threshold)
            db.Entry(d).Property(x => x.CreatedAt).CurrentValue = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();

            var sweep = scope.ServiceProvider.GetRequiredService<QuickShare.Features.Deliveries.ColdStorage.ColdStorageSweepJob>();
            await sweep.SweepAsync();
        }

        // 2. Verify ColdBytesTotal relief is reflected on /api/account/cold-storage and /account/usage
        var policyDto = await client.GetFromJsonAsync<QuickShare.Features.Deliveries.ColdStorage.ColdStoragePolicyResponse>(
            RoutePaths.AccountColdStorageApi);
        Assert.NotNull(policyDto);
        Assert.Equal(7, policyDto!.ColdStorageDays);
        Assert.Equal(fiveGb, policyDto.ColdBytesTotal);

        var usageHtml = await client.GetStringAsync(RoutePaths.AccountUsage);
        Assert.Contains("id=\"usage-cold-bytes\"", usageHtml);

        // 3. Direct download while Cold returns 409 Conflict, and recipient page renders #dl-cold-storage-panel
        var anonClient = _factory.CreateClient();
        var coldDlRes = await anonClient.GetAsync($"/api/downloads/{shareCode}");
        Assert.Equal(HttpStatusCode.Conflict, coldDlRes.StatusCode);

        var coldPage = await anonClient.GetStringAsync($"/d/{shareCode}");
        Assert.Contains("id=\"dl-cold-storage-panel\"", coldPage);
        Assert.Contains("id=\"dl-cold-restore-btn\"", coldPage);

        // 4. Trigger restore -> transitions back to Hot and becomes downloadable
        var restoreRes = await anonClient.PostAsync($"/api/downloads/{shareCode}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restoreRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restored = db.Deliveries.First(x => x.Id == deliveryId);
            Assert.Equal(DeliveryStorageTier.Hot, restored.StorageTier);
            Assert.NotNull(restored.RestoredUntil);
        }
    }

    // -----------------------------------------------------------------
    // #150 — Custom sending domain: SPF/DKIM/DMARC verification, custom From & fallback on revoke
    // -----------------------------------------------------------------

    [Fact]
    public async Task CustomSendingDomain_VerifiesDnsRecords_UsesCustomFromWhenVerified_AndFallsBackWhenRevoked()
    {
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(proKey);

        // 1. Configure custom sending domain share@studionam.vn
        var putRes = await client.PutAsJsonAsync(RoutePaths.AccountSendingDomainApi, new
        {
            domain = "studionam.vn",
            fromLocalPart = "share",
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var domainDto = await putRes.Content.ReadFromJsonAsync<QuickShare.Features.Email.SendingDomains.CustomSendingDomainDto>();
        Assert.NotNull(domainDto);
        Assert.Equal("share@studionam.vn", domainDto!.FullFromAddress);
        Assert.Equal(CustomSendingDomain.StatusPending, domainDto.Status);
        Assert.Contains("v=spf1", domainDto.SpfRecord);
        Assert.Contains("v=DKIM1", domainDto.DkimValue);
        Assert.Contains("v=DMARC1", domainDto.DmarcRecord);

        // 2. Before verification, Resolver returns null -> fallback to platform sender
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var beforeVerify = await QuickShare.Features.Email.SendingDomains.SendingDomainResolver.GetVerifiedFromAddressAsync(
                db, proUser.Id, null, CancellationToken.None);
            Assert.Null(beforeVerify);
        }

        // 3. Verify DNS records -> Status becomes Verified
        var verifyRes = await client.PostAsync(RoutePaths.AccountSendingDomainVerifyApi, null);
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifiedDto = await verifyRes.Content.ReadFromJsonAsync<QuickShare.Features.Email.SendingDomains.CustomSendingDomainDto>();
        Assert.NotNull(verifiedDto);
        Assert.Equal(CustomSendingDomain.StatusVerified, verifiedDto!.Status);
        Assert.True(verifiedDto.SpfVerified && verifiedDto.DkimVerified && verifiedDto.DmarcVerified);

        // 4. EmailSender uses share@studionam.vn when VerifiedFromAddress is supplied
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Email.EmailSender>();
            var fromAddr = await QuickShare.Features.Email.SendingDomains.SendingDomainResolver.GetVerifiedFromAddressAsync(
                db, proUser.Id, null, CancellationToken.None);
            Assert.Equal("share@studionam.vn", fromAddr);

            var msg = sender.BuildMessage(
                "client@acme.vn",
                "Client",
                "Delivery ready",
                "<p>Download</p>",
                new QuickShare.Infrastructure.Email.EmailBrandIdentity(SenderName: "Studio Nam", VerifiedFromAddress: fromAddr));
            Assert.Equal("share@studionam.vn", msg.From.Mailboxes.Single().Address);
        }

        // 5. Revoke custom sending domain -> falls back to default platform From address
        var delRes = await client.DeleteAsync(RoutePaths.AccountSendingDomainApi);
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var afterRevoke = await QuickShare.Features.Email.SendingDomains.SendingDomainResolver.GetVerifiedFromAddressAsync(
                db, proUser.Id, null, CancellationToken.None);
            Assert.Null(afterRevoke);
        }
    }

    // -----------------------------------------------------------------
    // #167 — In-app live chat: Pro/Studio priority queue, Free FAQ bot & email ticket escalation
    // -----------------------------------------------------------------

    [Fact]
    public async Task InAppLiveChat_RoutesPaidToPriorityQueue_FreeToFaqBot_AndEscalatesWithTicketId()
    {
        var freeKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(freeKey, PlanTierLimits.Free);
        var proKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);

        // 1. Free user opens chat -> routed to FAQ bot queue
        var freeClient = _factory.CreateAuthenticatedClient(freeKey);
        var freeMsgRes = await freeClient.PostAsJsonAsync(RoutePaths.SupportChatApi, new
        {
            body = "My upload is stuck at 50%, how do I resume?",
        });
        Assert.Equal(HttpStatusCode.OK, freeMsgRes.StatusCode);
        var freeThread = await freeMsgRes.Content.ReadFromJsonAsync<QuickShare.Features.Support.SupportChatThreadDto>();
        Assert.NotNull(freeThread);
        Assert.Equal(SupportChatThread.QueueFaqBot, freeThread!.QueueTier);
        Assert.StartsWith("SUP-", freeThread.TicketNumber);
        Assert.Equal(2, freeThread.Messages.Count);
        Assert.Contains("FAQ Bot", freeThread.Messages[1].Body);

        // 2. Pro user opens chat -> routed to Priority Live queue + escalates to email thread
        var proClient = _factory.CreateAuthenticatedClient(proKey);
        var proMsgRes = await proClient.PostAsJsonAsync(RoutePaths.SupportChatApi, new
        {
            body = "Need urgent help with VAT invoice billing",
        });
        Assert.Equal(HttpStatusCode.OK, proMsgRes.StatusCode);
        var proThread = await proMsgRes.Content.ReadFromJsonAsync<QuickShare.Features.Support.SupportChatThreadDto>();
        Assert.NotNull(proThread);
        Assert.Equal(SupportChatThread.QueuePriorityLive, proThread!.QueueTier);
        Assert.Contains("Priority Queue", proThread.Messages[1].Body);

        var escRes = await proClient.PostAsJsonAsync($"{RoutePaths.SupportChatApi}/escalate", new
        {
            email = "finance@studionam.vn",
        });
        Assert.Equal(HttpStatusCode.OK, escRes.StatusCode);
        var escThread = await escRes.Content.ReadFromJsonAsync<QuickShare.Features.Support.SupportChatThreadDto>();
        Assert.NotNull(escThread);
        Assert.Equal(SupportChatThread.StatusEscalated, escThread!.Status);
        Assert.Equal("finance@studionam.vn", escThread.EscalatedEmail);

        var pageHtml = await proClient.GetStringAsync(RoutePaths.AccountSupport);
        Assert.Contains("id=\"support-chat-card\"", pageHtml);
        Assert.Contains(proThread.TicketNumber, pageHtml);
    }

    // -----------------------------------------------------------------
    // #186 — Zapier + Make integration: catalog, scoped token + webhook connection & revocation
    // -----------------------------------------------------------------

    [Fact]
    public async Task ZapierAndMakeIntegration_PublishesCatalog_MintsScopedTokenAndWebhook_AndRevokesCleanly()
    {
        var key = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(key, PlanTierLimits.Pro);

        var anonClient = _factory.CreateClient();
        var catalog = await anonClient.GetFromJsonAsync<QuickShare.Features.Automations.AutomationCatalogResponse>(
            RoutePaths.AutomationsCatalogApi);
        Assert.NotNull(catalog);
        Assert.Contains(WebhookEvents.DeliveryCreated, catalog!.Triggers);
        Assert.NotEmpty(catalog.Templates);

        var client = _factory.CreateAuthenticatedClient(key);
        var createRes = await client.PostAsJsonAsync(RoutePaths.AutomationsApi, new
        {
            platform = AutomationConnection.PlatformZapier,
            name = "New Transfer -> Google Sheets Row",
            scopes = "read,write",
            webhookUrl = "https://hooks.zapier.com/hooks/catch/123456/abcdef",
            events = "delivery.created,delivery.expired",
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<QuickShare.Features.Automations.AutomationConnectionDto>();
        Assert.NotNull(created);
        Assert.Equal(AutomationConnection.PlatformZapier, created!.Platform);
        Assert.Equal("read,write", created.Scopes);
        Assert.NotNull(created.IssuedToken);
        Assert.StartsWith(ApiKey.TokenPrefix, created.IssuedToken);
        Assert.NotNull(created.WebhookEndpointId);

        var pageHtml = await client.GetStringAsync(RoutePaths.AccountAutomations);
        Assert.Contains("id=\"automations-card\"", pageHtml);
        Assert.Contains("New Transfer -&gt; Google Sheets Row", pageHtml);

        // Revoke connection -> disables both AutomationConnection, underlying ApiKey, and WebhookEndpoint
        var delRes = await client.DeleteAsync($"{RoutePaths.AutomationsApi}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var listAfter = await client.GetFromJsonAsync<List<QuickShare.Features.Automations.AutomationConnectionDto>>(RoutePaths.AutomationsApi);
        Assert.NotNull(listAfter);
        Assert.Single(listAfter!);
        Assert.False(listAfter[0].IsActive);
    }

    // -----------------------------------------------------------------
    // #164 & #177 — More payment methods: MoMo / ZaloPay / VISA via PayOS + Stripe & PayPal rails
    // -----------------------------------------------------------------

    [Fact]
    public async Task PaymentMethods_SupportsMoMoZaloPayVisaDeepLinks_AndStripePayPalCheckoutRails()
    {
        var key = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(key, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(key);

        // 1. MoMo wallet checkout session -> PayOS provider + momo:// deep link
        var momoRes = await client.PostAsJsonAsync(RoutePaths.CheckoutSessionApi, new
        {
            plan = PlanTierLimits.Pro,
            billing = BillingPeriods.Monthly,
            paymentMethod = PaymentMethods.MoMo,
            currency = PricingKeys.DefaultCurrency,
        });
        Assert.Equal(HttpStatusCode.OK, momoRes.StatusCode);
        var momoDto = await momoRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.CheckoutSessionResponse>();
        Assert.NotNull(momoDto);
        Assert.Equal(PaymentProviders.PayOS, momoDto!.Provider);
        Assert.Equal(PaymentMethods.MoMo, momoDto.PaymentMethod);
        Assert.StartsWith("momo://", momoDto.DeepLinkUrl);

        // 2. Stripe international card checkout session -> Stripe provider + USD currency
        var stripeRes = await client.PostAsJsonAsync(RoutePaths.CheckoutSessionApi, new
        {
            plan = PlanTierLimits.Studio,
            billing = BillingPeriods.Yearly,
            paymentMethod = PaymentMethods.StripeCard,
            currency = PricingKeys.CurrencyUsd,
        });
        Assert.Equal(HttpStatusCode.OK, stripeRes.StatusCode);
        var stripeDto = await stripeRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.CheckoutSessionResponse>();
        Assert.NotNull(stripeDto);
        Assert.Equal(PaymentProviders.Stripe, stripeDto!.Provider);
        Assert.Equal(PaymentMethods.StripeCard, stripeDto.PaymentMethod);
        Assert.Contains("checkout.stripe.com", stripeDto.CheckoutUrl);

        // 3. PayPal wallet checkout session -> PayPal provider
        var paypalRes = await client.PostAsJsonAsync(RoutePaths.CheckoutSessionApi, new
        {
            plan = PlanTierLimits.Pro,
            billing = BillingPeriods.Monthly,
            paymentMethod = PaymentMethods.PayPalWallet,
            currency = PricingKeys.CurrencyEur,
        });
        Assert.Equal(HttpStatusCode.OK, paypalRes.StatusCode);
        var paypalDto = await paypalRes.Content.ReadFromJsonAsync<QuickShare.Features.Payments.CheckoutSessionResponse>();
        Assert.NotNull(paypalDto);
        Assert.Equal(PaymentProviders.PayPal, paypalDto!.Provider);
        Assert.Equal(PaymentMethods.PayPalWallet, paypalDto.PaymentMethod);
        Assert.Contains("paypal.com", paypalDto.CheckoutUrl);

        var upgradeHtml = await client.GetStringAsync(RoutePaths.Upgrade);
        Assert.Contains("id=\"upgrade-payment-method\"", upgradeHtml);
    }

    // -----------------------------------------------------------------
    // #118 — Passkey recovery codes: generate 10 single-use codes, spend on login, require re-add
    // -----------------------------------------------------------------

    [Fact]
    public async Task PasskeyRecoveryCodes_GeneratesTenCodes_AuthenticatesSingleUse_AndFlagsReAdd()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var uniqueEmail = $"passkey-{user.Id:N}@quickshare.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = db.Users.First(x => x.Id == user.Id);
            u.Email = uniqueEmail;
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient(key);

        var genRes = await client.PostAsync(RoutePaths.AccountPasskeyRecoveryCodesApi, null);
        Assert.Equal(HttpStatusCode.OK, genRes.StatusCode);
        var genDto = await genRes.Content.ReadFromJsonAsync<QuickShare.Features.Auth.Passkeys.PasskeyRecoveryGenerateDto>();
        Assert.NotNull(genDto);
        Assert.Equal(10, genDto!.Codes.Count);

        var firstCode = genDto.Codes[0];

        // Authenticate via anonymous client using email + recovery code
        var anonClient = _factory.CreateClient();
        var loginRes = await anonClient.PostAsJsonAsync(RoutePaths.PasskeyRecoveryLoginApi, new
        {
            email = uniqueEmail,
            code = firstCode,
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // Second attempt with the same single-use code is rejected
        var replayRes = await anonClient.PostAsJsonAsync(RoutePaths.PasskeyRecoveryLoginApi, new
        {
            email = uniqueEmail,
            code = firstCode,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);

        // Status reflects 9 remaining codes and RequirePasskeyReAdd = true
        var statusDto = await client.GetFromJsonAsync<QuickShare.Features.Auth.Passkeys.PasskeyRecoveryStatusDto>(RoutePaths.AccountPasskeyRecoveryCodesApi);
        Assert.NotNull(statusDto);
        Assert.Equal(9, statusDto!.CodesRemaining);
        Assert.True(statusDto.RequirePasskeyReAdd);
    }

    // -----------------------------------------------------------------
    // #313 — Encryption key management: workspace policy, PBKDF2 passphrase & key escrow
    // -----------------------------------------------------------------

    [Fact]
    public async Task EncryptionKeyManagement_EnforcesWorkspacePolicy_AndStoresKeyEscrowForAuthorizedCaller()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(ownerKey);

        // Create workspace and set EncryptionPolicy = required
        var createWs = await client.PostAsJsonAsync("/api/workspace", new
        {
            name = "Zero-Knowledge Vault Team",
            seats = 3,
        });
        Assert.Equal(HttpStatusCode.OK, createWs.StatusCode);

        var patchWs = await client.PatchAsJsonAsync("/api/workspace", new
        {
            encryptionPolicy = WorkspaceEncryptionPolicy.Required,
        });
        Assert.Equal(HttpStatusCode.OK, patchWs.StatusCode);

        var shareCode = $"enc{owner.Id.ToString("N")[..7]}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = owner.Id,
                ShareCode = shareCode,
                FileName = "board-deck.pdf",
                ContentType = "application/pdf",
                FileSize = 2048,
                TotalBytes = 2048,
                StoragePath = $"uploads/{owner.Id:N}/{shareCode}/board-deck.pdf",
                MaxDownloads = 10,
                Status = DeliveryStatus.Active,
                EncryptionMode = DeliveryEncryptionMode.Passphrase,
                EncryptionKdfSalt = "YWJjZGVmZ2hpamtsbW5vcA==",
                EncryptionKdfIterations = 600000,
                EncryptedKeyEscrow = "escrow-wrapped-cek-v1",
                ExpiresAt = DateTime.UtcNow.AddDays(5),
            });
            await db.SaveChangesAsync();
        }

        var dlPage = await client.GetStringAsync($"/d/{shareCode}");
        Assert.Contains("\"encryptionMode\":\"passphrase\"", dlPage);
        Assert.Contains("\"encryptionKdfSalt\":\"YWJjZGVmZ2hpamtsbW5vcA==\"", dlPage);
        Assert.Contains("\"hasKeyEscrow\":true", dlPage);
        Assert.Contains("id=\"dl-passphrase-vault\"", dlPage);

        var escrowRes = await client.GetFromJsonAsync<QuickShare.Features.Downloads.KeyEscrow.DeliveryKeyEscrowDto>($"/api/downloads/{shareCode}/key-escrow");
        Assert.NotNull(escrowRes);
        Assert.Equal("escrow-wrapped-cek-v1", escrowRes!.EncryptedKeyEscrow);
    }

    // -----------------------------------------------------------------
    // #84 & #152 — Multi-locale (zh, ja, es, ar) + RTL layout & SEO landing pages
    // -----------------------------------------------------------------

    [Fact]
    public async Task MultiLocaleAndRtl_RendersArabicRtlDirection_AndLocalizedSeoLandingPages()
    {
        var client = _factory.CreateClient();

        // Request with Accept-Language: ar renders dir="rtl" and lang="ar"
        using var req = new HttpRequestMessage(HttpMethod.Get, RoutePaths.SeoArSendLargeFilesFree);
        req.Headers.Add("Accept-Language", "ar");
        var arRes = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, arRes.StatusCode);
        var arHtml = await arRes.Content.ReadAsStringAsync();
        Assert.Contains("dir=\"rtl\"", arHtml);
        Assert.Contains("lang=\"ar\"", arHtml);

        // Verify zh, ja, es SEO landing routes also render 200 OK and appear in sitemap.xml
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(RoutePaths.SeoZhSendLargeFilesFree)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(RoutePaths.SeoJaSendLargeFilesFree)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(RoutePaths.SeoEsSendLargeFilesFree)).StatusCode);

        var sitemapXml = await client.GetStringAsync(RoutePaths.Sitemap);
        Assert.Contains(RoutePaths.SeoZhSendLargeFilesFree, sitemapXml);
        Assert.Contains(RoutePaths.SeoArSendLargeFilesFree, sitemapXml);
    }

    // -----------------------------------------------------------------
    // #188 — Annual external penetration test posture & admin report recording
    // -----------------------------------------------------------------

    [Fact]
    public async Task PenetrationTestPosture_ReturnsBaselineAssessment_AndRendersSecurityPostureSection()
    {
        var client = _factory.CreateClient();

        var posture = await client.GetFromJsonAsync<QuickShare.Features.Security.GetPenetrationTestPostureResponse>(RoutePaths.SecurityPentestApi);
        Assert.NotNull(posture);
        Assert.NotNull(posture!.Latest);
        Assert.Equal(0, posture.Latest.CriticalCount);
        Assert.Equal(0, posture.Latest.HighCount);
        Assert.Equal(PenetrationTestRecord.StatusVerifiedClean, posture.Latest.Status);

        var secHtml = await client.GetStringAsync(RoutePaths.Security);
        Assert.Contains("id=\"pentest-posture\"", secHtml);
    }

    // -----------------------------------------------------------------
    // #163 — BYOK encryption: customer-managed KMS key config, rotation, region check & revoke
    // -----------------------------------------------------------------

    [Fact]
    public async Task ByokEncryption_ConfiguresRotatesEnforcesRegion_AndBlocksAccessOnRevoke()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(ownerKey);

        var createWs = await client.PostAsJsonAsync("/api/workspace", new
        {
            name = "BYOK Sovereign Vault",
            seats = 3,
        });
        Assert.Equal(HttpStatusCode.OK, createWs.StatusCode);

        // Configure AWS KMS key in region "eu"
        var putRes = await client.PutAsJsonAsync(RoutePaths.WorkspaceByokApi, new
        {
            provider = WorkspaceByokConfig.ProviderAwsKms,
            keyId = "arn:aws:kms:eu-central-1:123456789012:key/11112222-3333-4444-5555-666677778888",
            roleArn = "arn:aws:iam::123456789012:role/QuickShareByokRole",
            keyRegion = "eu",
        });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var byok = await putRes.Content.ReadFromJsonAsync<QuickShare.Features.Workspaces.Byok.WorkspaceByokDto>();
        Assert.NotNull(byok);
        Assert.True(byok!.Configured);
        Assert.Equal(1, byok.KeyVersion);
        Assert.Equal(WorkspaceByokConfig.StatusActive, byok.Status);

        // Rotate key -> KeyVersion increments to 2
        var rotRes = await client.PostAsJsonAsync(RoutePaths.WorkspaceByokRotateApi, new
        {
            keyId = "arn:aws:kms:eu-central-1:123456789012:key/99990000-aaaa-bbbb-cccc-ddddeeeeffff",
        });
        Assert.Equal(HttpStatusCode.OK, rotRes.StatusCode);
        var rotated = await rotRes.Content.ReadFromJsonAsync<QuickShare.Features.Workspaces.Byok.WorkspaceByokDto>();
        Assert.NotNull(rotated);
        Assert.Equal(2, rotated!.KeyVersion);

        // Seed a delivery bound to this workspace and verify access succeeds while active, then fails after revoke
        Guid workspaceId;
        var shareCode = $"byok{owner.Id.ToString("N")[..6]}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ws = db.Workspaces.First(w => w.OwnerId == owner.Id);
            workspaceId = ws.Id;
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = owner.Id,
                WorkspaceId = workspaceId,
                ShareCode = shareCode,
                FileName = "sovereign-contract.pdf",
                ContentType = "application/pdf",
                FileSize = 4096,
                TotalBytes = 4096,
                StoragePath = $"uploads/eu/{owner.Id:N}/{shareCode}/sovereign-contract.pdf",
                StorageRegion = "eu",
                MaxDownloads = 10,
                Status = DeliveryStatus.Active,
                ByokKeyId = rotated.KeyId,
                ByokKeyVersion = rotated.KeyVersion,
                ByokWrappedDek = QuickShare.Features.Workspaces.Byok.GetWorkspaceByokEndpoint.WrapEnvelopeKey(rotated.KeyId!, rotated.KeyVersion, Guid.NewGuid()),
                EncryptedKeyEscrow = "escrow-cek-byok",
                ExpiresAt = DateTime.UtcNow.AddDays(5),
            });
            await db.SaveChangesAsync();
        }

        // Before revoke: key escrow is accessible
        var escrowBefore = await client.GetAsync($"/api/downloads/{shareCode}/key-escrow");
        Assert.Equal(HttpStatusCode.OK, escrowBefore.StatusCode);

        // Revoke BYOK key -> immediate access revocation
        var revRes = await client.PostAsync(RoutePaths.WorkspaceByokRevokeApi, null);
        Assert.Equal(HttpStatusCode.OK, revRes.StatusCode);

        var escrowAfter = await client.GetAsync($"/api/downloads/{shareCode}/key-escrow");
        Assert.Equal(HttpStatusCode.Gone, escrowAfter.StatusCode);

        var dlHtml = await client.GetStringAsync($"/d/{shareCode}");
        Assert.Contains("id=\"download-phase-revoked\"", dlHtml);
    }

    // -----------------------------------------------------------------
    // #109 — Cloud import: stream from Google Drive / Dropbox / OneDrive without storing credentials
    // -----------------------------------------------------------------

    [Fact]
    public async Task CloudImport_StreamsFromProvider_RecordsQuotaAndAudit_WithoutStoringCredentials()
    {
        var proKey = Guid.CreateVersion7().ToString();
        var proUser = await _factory.SeedUserAsync(proKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(proKey);

        var importRes = await client.PostAsJsonAsync(RoutePaths.CloudImportApi, new
        {
            provider = "google_drive",
            fileName = "quarterly-roadmap.pdf",
            downloadUrl = "https://www.googleapis.com/drive/v3/files/1a2b3c4d5e?alt=media",
            contentType = "application/pdf",
        });
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);
        var imported = await importRes.Content.ReadFromJsonAsync<QuickShare.Features.Uploads.CloudImport.CloudImportResponse>();
        Assert.NotNull(imported);
        Assert.Equal("google_drive", imported!.Provider);
        Assert.Equal("quarterly-roadmap.pdf", imported.FileName);
        Assert.True(imported.FileSize > 0);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = db.Deliveries.FirstOrDefault(d => d.ShareCode == imported.ShareCode);
            Assert.NotNull(delivery);
            Assert.Equal(DeliverySources.Cloud, delivery!.Source);

            var auditExists = db.SecurityEvents.Any(e =>
                e.UserId == proUser.Id && e.EventType == SecurityEventType.CloudImportCompleted);
            Assert.True(auditExists);
        }
    }

    // -----------------------------------------------------------------
    // #279, #80, #26 — PWA offline delivery bundle, privacy-mode guard & offline upload queue check
    // -----------------------------------------------------------------

    [Fact]
    public async Task PwaOfflineReadAndQueue_ServesOfflineBundle_BlocksPrivacyMode_AndValidatesOfflineUploadQueue()
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

        // 3. Offline upload queue preflight validates queued items against plan & quota
        var queueRes = await client.PostAsJsonAsync(RoutePaths.OfflineUploadQueueApi, new
        {
            items = new[]
            {
                new { fileName = "site-photo-1.jpg", fileSize = 1024L * 1024L, contentType = "image/jpeg" },
                new { fileName = "huge-archive.bin", fileSize = 50L * 1024L * 1024L * 1024L, contentType = "application/octet-stream" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queueCheck = await queueRes.Content.ReadFromJsonAsync<QuickShare.Features.Downloads.Offline.OfflineQueueCheckResponse>();
        Assert.NotNull(queueCheck);
        Assert.Equal(1, queueCheck!.AllowedCount);
        Assert.Equal(1, queueCheck.RejectedCount);
        Assert.True(queueCheck.Items[0].Allowed);
        Assert.False(queueCheck.Items[1].Allowed);
    }
}