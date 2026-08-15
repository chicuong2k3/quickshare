using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Analytics;
using QuickShare.Features.Analytics.Dashboard;
using QuickShare.Features.Shares.P2P;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// E2E coverage for issue #24 analytics: anonymous event tracking at every
/// lifecycle point (upload, download, P2P, OTP, ad, signup), the client
/// beacon endpoint, and the admin-only founder dashboard.
/// </summary>
public sealed class AnalyticsTrackingTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "report.pdf";
    private const string TestContentType = "application/pdf";
    private const long TestFileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public AnalyticsTrackingTests(TestWebAppFactory factory) => _factory = factory;

    private async Task ClearEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AnalyticsEvents.ExecuteDeleteAsync();
    }

    private async Task<List<AnalyticsEvent>> GetEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AnalyticsEvents.AsNoTracking().ToListAsync();
    }

    private async Task<CommitR2Response> UploadFileAsync(HttpClient client, string? otp = null)
    {
        var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        })).Content.ReadFromJsonAsync<StartR2Response>();

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start!.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            Otp = otp,
        });
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        return commit!;
    }

    private HttpClient CreateNoRedirectClient(bool authenticated = false)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authenticated)
            client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, TestWebAppFactory.TestUserProviderKey);
        return client;
    }

    // ── Client beacon endpoint ──

    [Fact]
    public async Task Track_Visitor_SetsCookieAndRecordsEvent()
    {
        await ClearEventsAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/analytics/track", new { Event = AnalyticsEvents.Visitor });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var visitorCookie = response.Headers
            .GetValues("Set-Cookie")
            .SingleOrDefault(c => c.StartsWith($"{AnalyticsConstants.VisitorCookieName}=", StringComparison.Ordinal));
        Assert.NotNull(visitorCookie);

        var events = await GetEventsAsync();
        var visitorEvent = events.Single(e => e.EventName == AnalyticsEvents.Visitor);
        Assert.NotNull(visitorEvent.VisitorId);
    }

    [Fact]
    public async Task Track_UnknownEvent_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/analytics/track", new { Event = "not_a_real_event" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Upload lifecycle ──

    [Fact]
    public async Task UploadFlow_RecordsStartedAndCompleted()
    {
        await ClearEventsAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, TestWebAppFactory.TestUserProviderKey);

        await UploadFileAsync(client);

        var events = await GetEventsAsync();
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.UploadStarted);
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.UploadCompleted);

        var completed = events.Single(e => e.EventName == AnalyticsEvents.UploadCompleted);
        Assert.Equal(TestFileName, completed.FileName);
        Assert.Equal(TestFileSize, completed.FileSize);
        Assert.Equal(TestContentType, completed.ContentType);
    }

    // ── Download lifecycle ──

    [Fact]
    public async Task Download_RecordsCompleted()
    {
        await ClearEventsAsync();
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client);

        var download = await client.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);

        // Counter + analytics run on a background task — poll briefly.
        var recorded = false;
        for (var i = 0; i < 20 && !recorded; i++)
        {
            await Task.Delay(100);
            var events = await GetEventsAsync();
            recorded = events.Any(e => e.EventName == AnalyticsEvents.DownloadCompleted);
        }
        Assert.True(recorded, "download_completed not recorded within 2s");

        var events2 = await GetEventsAsync();
        var downloadEvent = events2.Single(e => e.EventName == AnalyticsEvents.DownloadCompleted);
        Assert.Equal(TestFileName, downloadEvent.FileName);
    }

    // ── OTP gate ──

    [Fact]
    public async Task VerifyOtp_RecordsPassAndFail()
    {
        await ClearEventsAsync();
        const string otp = "123456";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, TestWebAppFactory.TestUserProviderKey);
        var commit = await UploadFileAsync(client, otp);

        var wrong = await client.PostAsJsonAsync($"/api/deliveries/{commit.ShareCode}/verify-otp", new { Otp = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var right = await client.PostAsJsonAsync($"/api/deliveries/{commit.ShareCode}/verify-otp", new { Otp = otp });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);

        var events = await GetEventsAsync();
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.OtpFail);
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.OtpPass);
    }

    // ── P2P session ──

    [Fact]
    public async Task CreateP2PShare_RecordsSessionCreated()
    {
        await ClearEventsAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "movie.mp4",
            FileSize = 1024L,
            MimeType = "video/mp4",
            BaseUrl = "http://localhost",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await GetEventsAsync();
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.P2pSessionCreated);
    }

    [Fact]
    public async Task P2PReceiverJoins_RecordsReceiverJoined()
    {
        await ClearEventsAsync();
        var client = _factory.CreateClient();
        var share = await (await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "movie.mp4",
            FileSize = 1024L,
            MimeType = "video/mp4",
            BaseUrl = "http://localhost",
        })).Content.ReadFromJsonAsync<CreateP2PShareResponse>();

        using var ws = await _factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri($"ws://localhost/ws/connect/{share!.ShareCode}"), CancellationToken.None);

        var register = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = P2PProtocol.TypeRegister, role = P2PProtocol.RoleReceiver }));
        await ws.SendAsync(register, WebSocketMessageType.Text, true, CancellationToken.None);

        // Registration completes when the server responds 'registered' — only
        // then has the receiver branch (and its analytics call) run.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[8 * 1024];
        var received = await ws.ReceiveAsync(buffer, cts.Token);
        var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
        var message = JsonDocument.Parse(text).RootElement;
        Assert.Equal(P2PProtocol.TypeRegistered, message.GetProperty(P2PProtocol.PropType).GetString());

        var recorded = false;
        for (var i = 0; i < 20 && !recorded; i++)
        {
            await Task.Delay(100);
            var events = await GetEventsAsync();
            recorded = events.Any(e => e.EventName == AnalyticsEvents.P2pReceiverJoined);
        }
        Assert.True(recorded, "p2p_receiver_joined not recorded within 2s");

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    // ── Signup conversion ──

    [Fact]
    public async Task RegisterNewUser_RecordsSignup()
    {
        await ClearEventsAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();

        await UserRegistrationService.RegisterAsync(
            "test", Guid.NewGuid().ToString(), "New User", "new@example.com",
            null, db, CancellationToken.None, analytics);

        var events = await GetEventsAsync();
        Assert.Contains(events, e => e.EventName == AnalyticsEvents.Signup);
    }

    // ── Dashboard ──

    [Fact]
    public async Task Dashboard_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/analytics");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_AuthenticatedNonAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, "nobody@example.com");
        var response = await client.GetAsync("/api/admin/analytics");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_Admin_ReturnsAggregates()
    {
        await ClearEventsAsync();
        var client = _factory.CreateAuthenticatedClient();

        var track = await client.PostAsJsonAsync("/api/analytics/track", new { Event = AnalyticsEvents.Visitor });
        Assert.Equal(HttpStatusCode.NoContent, track.StatusCode);

        var response = await client.GetAsync("/api/admin/analytics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<AnalyticsDashboardResponse>();

        Assert.NotNull(data);
        Assert.True(data.Funnel.Visitor >= 1, "funnel.visitor should count the tracked visitor");
        Assert.True(data.DailyActiveUsers.Count > 0, "DAU series should have today's row");
        Assert.True(data.DailyActiveUsers.Sum(d => d.Count) >= 1, "DAU should count distinct visitors");
    }

    [Fact]
    public async Task Dashboard_TopFilesAndGeo_AfterDownload()
    {
        await ClearEventsAsync();
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client);

        // Simulate a recipient (different visitor) downloading with a country header.
        var recipient = CreateNoRedirectClient();
        var download = await recipient.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);

        var recorded = false;
        for (var i = 0; i < 20 && !recorded; i++)
        {
            await Task.Delay(100);
            var events = await GetEventsAsync();
            recorded = events.Any(e => e.EventName == AnalyticsEvents.DownloadCompleted);
        }
        Assert.True(recorded, "download_completed not recorded within 2s");

        // Seed a country-tagged event directly (CF-IPCountry header is a Cloudflare
        // proxy concern the TestServer does not set).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                Id = Guid.CreateVersion7(),
                EventName = AnalyticsEvents.DownloadCompleted,
                CountryCode = "VN",
                FileName = TestFileName,
                FileSize = TestFileSize,
            });
            await db.SaveChangesAsync();
        }

        var admin = _factory.CreateAuthenticatedClient();
        var data = await admin.GetFromJsonAsync<AnalyticsDashboardResponse>("/api/admin/analytics");

        Assert.NotNull(data);
        Assert.Contains(data.TopFiles, f => f.FileName == TestFileName);
        Assert.Contains(data.GeographicSpread, g => g.CountryCode == "VN");
    }

    // ── Admin page smoke ──

    [Fact]
    public async Task AdminAnalyticsPage_Anonymous_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(RoutePaths.AdminAnalytics);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AdminAnalyticsPage_Admin_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(RoutePaths.AdminAnalytics);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
