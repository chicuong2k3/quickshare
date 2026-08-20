using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Extend;
using QuickShare.Features.Users.Retention;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Workspace retention policy (issue #161): an account-wide ceiling on how long any
/// share may live. The invariant under test is that the policy is a ceiling and never
/// a floor — a sender's shorter choice always survives, and clamping is not an error.
///
/// Coverage is split by the paths that can produce an expiry: the settings mutation,
/// upload, extend, the approve-extension path, and the sweep that converges rows
/// created before the policy existed.
/// </summary>
public sealed class RetentionPolicyApiTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "policy.bin";
    private const string TestContentType = "application/octet-stream";
    private const long TestFileSize = 1024;
    private const int WindowDays = 14;

    private readonly TestWebAppFactory _factory;

    public RetentionPolicyApiTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Update_OnPaidPlan_PersistsWindowAndAudits()
    {
        var (client, userId) = await PaidClientAsync();

        var response = await client.PutAsJsonAsync("/api/account/retention",
            new UpdateRetentionPolicyRequest { RetentionDays = WindowDays, NoExtend = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpdateRetentionPolicyResponse>();
        Assert.NotNull(payload);
        Assert.Equal(WindowDays, payload.RetentionDays);
        Assert.True(payload.NoExtend);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(WindowDays, user.RetentionDays);
        Assert.True(user.RetentionNoExtend);

        var audit = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.EventType == SecurityEventType.RetentionPolicyChanged)
            .ToListAsync();
        Assert.Contains(audit, e => e.MetadataJson != null && e.MetadataJson.Contains("14", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_OnFreePlan_Returns403AndLeavesAccountUnpolicied()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PutAsJsonAsync("/api/account/retention",
            new UpdateRetentionPolicyRequest { RetentionDays = WindowDays });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).RetentionDays);
    }

    [Fact]
    public async Task Update_OutOfRangeWindow_Returns400()
    {
        var (client, userId) = await PaidClientAsync();

        foreach (var days in new[] { 0, -1, RetentionPolicy.MaxDays + 1 })
        {
            var response = await client.PutAsJsonAsync("/api/account/retention",
                new UpdateRetentionPolicyRequest { RetentionDays = days });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).RetentionDays);
    }

    [Fact]
    public async Task Update_ClearingWindow_AlsoClearsTheExtensionLock()
    {
        var (client, userId) = await PaidClientAsync();
        await SetPolicyAsync(client, WindowDays, noExtend: true);

        var cleared = await client.PutAsJsonAsync("/api/account/retention",
            new UpdateRetentionPolicyRequest { RetentionDays = null, NoExtend = true });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Null(user.RetentionDays);
        // A stranded lock with no ceiling to lock against would refuse extends forever.
        Assert.False(user.RetentionNoExtend);
    }

    [Fact]
    public async Task Upload_ClampsSenderChoiceToTheWindow()
    {
        var (client, _) = await PaidClientAsync();
        await SetPolicyAsync(client, WindowDays);

        // Asks for 30 days; the policy ceiling is 14.
        var delivery = await UploadFileAsync(client, expiryHours: 30 * 24);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.NotNull(row.ExpiresAt);
        Assert.True(row.ExpiresAt < DateTime.UtcNow.AddDays(WindowDays).AddMinutes(1),
            $"expiry {row.ExpiresAt} was not clamped to the {WindowDays}d window");
        Assert.True(row.ExpiresAt > DateTime.UtcNow.AddDays(WindowDays - 1),
            $"expiry {row.ExpiresAt} was cut below the window — the policy is a ceiling, not a replacement");
    }

    [Fact]
    public async Task Upload_ShorterSenderChoiceSurvivesTheWindow()
    {
        var (client, _) = await PaidClientAsync();
        await SetPolicyAsync(client, WindowDays);

        var delivery = await UploadFileAsync(client, expiryHours: 24);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.NotNull(row.ExpiresAt);
        Assert.True(row.ExpiresAt < DateTime.UtcNow.AddDays(2),
            $"expiry {row.ExpiresAt} was pushed out to the ceiling — a shorter choice must win");
    }

    [Fact]
    public async Task Extend_ClampsToTheWindow()
    {
        var (client, userId) = await PaidClientAsync();
        var delivery = await UploadFileAsync(client, expiryHours: 24);
        await SetPolicyAsync(client, RetentionPolicy.MinDays);

        var response = await client.PostAsync($"/api/deliveries/{delivery}/extend", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExtendDeliveryResponse>();
        Assert.NotNull(payload);

        // The extend grants 7 days; a 1-day window must pull it back.
        Assert.True(payload.ExpiresAt < DateTime.UtcNow.AddDays(RetentionPolicy.MinDays).AddMinutes(1),
            $"extend returned {payload.ExpiresAt}, beyond the {RetentionPolicy.MinDays}d window");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.Equal(payload.ExpiresAt, row.ExpiresAt);
        Assert.Equal(userId, row.SenderId);
    }

    [Fact]
    public async Task Extend_UnderRegulatedMode_IsRefusedRatherThanSilentlyNoOped()
    {
        var (client, _) = await PaidClientAsync();
        var delivery = await UploadFileAsync(client, expiryHours: 30 * 24);
        await SetPolicyAsync(client, RetentionPolicy.MinDays, noExtend: true);

        var response = await client.PostAsync($"/api/deliveries/{delivery}/extend", null);

        // A clamp would hand back a date at the ceiling and read as "extended" to the caller.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ErrorCodes.RetentionPolicyLocked, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sweep_ConvergesRowsCreatedBeforeThePolicy()
    {
        var (client, userId) = await PaidClientAsync();
        // Created while unpolicied: a 60-day expiry the policy would never have allowed.
        var stale = await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60));
        var compliant = await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(2));
        var compliantExpiry = await ExpiryOfAsync(compliant);

        await SetPolicyAsync(client, WindowDays);

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new DeliveryExpiryJob(scopeFactory, new LocalDiskStorageProviderStub(),
            NullLogger<DeliveryExpiryJob>.Instance);
        await job.ExpireDeliveriesAsync();

        var staleExpiry = await ExpiryOfAsync(stale);
        Assert.NotNull(staleExpiry);
        Assert.True(staleExpiry < DateTime.UtcNow.AddDays(WindowDays).AddMinutes(1),
            $"row still expires at {staleExpiry}, beyond the {WindowDays}d window");

        // Already inside the window: the sweep must not touch it.
        Assert.Equal(compliantExpiry, await ExpiryOfAsync(compliant));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(await db.SecurityEvents.AsNoTracking()
                .Where(e => e.UserId == userId && e.EventType == SecurityEventType.RetentionEnforced)
                .ToListAsync(),
            e => e.MetadataJson != null && e.MetadataJson.Contains("affected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sweep_LeavesHeldRowsAlone()
    {
        var (client, userId) = await PaidClientAsync();
        var held = await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60), legalHold: true);
        var heldExpiry = await ExpiryOfAsync(held);

        await SetPolicyAsync(client, WindowDays);

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new DeliveryExpiryJob(scopeFactory, new LocalDiskStorageProviderStub(),
            NullLogger<DeliveryExpiryJob>.Instance);
        await job.ExpireDeliveriesAsync();

        // A hold (issue #266) outranks the policy — shortening the expiry would only hand
        // the purge an earlier date on evidence somebody is relying on.
        Assert.Equal(heldExpiry, await ExpiryOfAsync(held));
    }

    [Fact]
    public async Task Preview_CountsWhatTheWindowWouldShortenWithoutMutating()
    {
        var (client, userId) = await PaidClientAsync();
        var stale = await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60));
        var compliant = await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(2));
        var staleExpiry = await ExpiryOfAsync(stale);

        var preview = await client.GetFromJsonAsync<RetentionPreviewResponse>(
            $"/api/account/retention/preview?days={WindowDays}");

        Assert.NotNull(preview);
        Assert.Equal(WindowDays, preview.Days);
        Assert.Equal(1, preview.Affected);
        Assert.Single(preview.Samples);
        Assert.Equal(staleExpiry, preview.EarliestExpiry);
        Assert.Equal(staleExpiry, preview.LatestExpiry);

        // Read-only: the dry run must not clamp anything it reports.
        Assert.Equal(staleExpiry, await ExpiryOfAsync(stale));
        Assert.NotNull(await ExpiryOfAsync(compliant));
    }

    [Fact]
    public async Task Preview_ExcludesHeldRows()
    {
        var (client, userId) = await PaidClientAsync();
        await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60), legalHold: true);

        var preview = await client.GetFromJsonAsync<RetentionPreviewResponse>(
            $"/api/account/retention/preview?days={WindowDays}");

        Assert.NotNull(preview);
        // The sweep will never clamp a held row, so promising it here would be a lie.
        Assert.Equal(0, preview.Affected);
        Assert.Empty(preview.Samples);
    }

    [Fact]
    public async Task Preview_WithoutWindow_ShortensNothing()
    {
        var (client, userId) = await PaidClientAsync();
        await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60));

        var preview = await client.GetFromJsonAsync<RetentionPreviewResponse>("/api/account/retention/preview");

        Assert.NotNull(preview);
        Assert.Null(preview.Days);
        Assert.Equal(0, preview.Affected);
        Assert.Empty(preview.Samples);
    }

    [Fact]
    public async Task Preview_OutOfRangeWindow_Returns400()
    {
        var (client, _) = await PaidClientAsync();

        var response = await client.GetAsync($"/api/account/retention/preview?days={RetentionPolicy.MaxDays + 1}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SettingsCard_ForPaidAccount_OffersEveryPresetAndPreselectsTheSavedWindow()
    {
        var (client, _) = await PaidClientAsync();
        await SetPolicyAsync(client, WindowDays, noExtend: true);

        var html = await GetHtmxAsync(client, RoutePaths.AccountSecurityActivity);

        Assert.Contains("account-retention__form", html, StringComparison.Ordinal);
        foreach (var preset in RetentionPolicy.Presets)
            Assert.Contains($"value=\"{preset}\"", html, StringComparison.Ordinal);

        // The saved state must come back selected, or a reload silently offers to clear it.
        Assert.Contains($"value=\"{WindowDays}\" selected", html, StringComparison.Ordinal);
        Assert.Contains("checked", html, StringComparison.Ordinal);

        // Paid: the controls are live and the upgrade hint is gone.
        Assert.DoesNotContain("Retention policies are a Pro and Studio feature.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("account-retention__locked", html, StringComparison.Ordinal);

        // Copy is localized on the way out, not left as resx keys.
        Assert.Contains("Maximum share lifetime", html, StringComparison.Ordinal);
        Assert.DoesNotContain("account_retention_", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsCard_ForFreeAccount_IsInertWithAnUpgradePath()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await GetHtmxAsync(client, RoutePaths.AccountSecurityActivity);

        Assert.Contains("account-retention__locked", html, StringComparison.Ordinal);
        Assert.Contains("Retention policies are a Pro and Studio feature.", html, StringComparison.Ordinal);
        // Inert, not hidden: a free owner should see the control they would be buying.
        Assert.Contains("account-retention__form", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.Contains(RoutePaths.Upgrade, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPanel_ForProposedWindow_NamesTheRowsItWouldShorten()
    {
        var (client, userId) = await PaidClientAsync();
        await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(60));

        var html = await GetHtmxAsync(client, $"{RoutePaths.AccountSecurityActivity}?retentionDays={WindowDays}");

        Assert.Contains("account-retention__preview", html, StringComparison.Ordinal);
        Assert.Contains("1 shares would expire sooner.", html, StringComparison.Ordinal);
        Assert.Contains(TestFileName, html, StringComparison.Ordinal);

        // Selecting a window is a dry run: the panel may claim the rows would move, but
        // nothing is saved until the owner submits the form.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).RetentionDays);
    }

    [Fact]
    public async Task PreviewPanel_WhenNothingWouldMove_SaysSoRatherThanShowingAnEmptyList()
    {
        var (client, userId) = await PaidClientAsync();
        await SeedDeliveryAsync(userId, DateTime.UtcNow.AddDays(2));

        var html = await GetHtmxAsync(client, $"{RoutePaths.AccountSecurityActivity}?retentionDays={WindowDays}");

        Assert.Contains("Nothing would change", html, StringComparison.Ordinal);
        Assert.DoesNotContain("would expire sooner", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsCard_WithoutTheHtmxHeader_RedirectsToAccount()
    {
        var (client, _) = await PaidClientAsync();

        var response = await client.GetAsync(RoutePaths.AccountSecurityActivity);

        // The page only exists as a partial; a direct hit must land on the tab that hosts it
        // instead of rendering a card with no shell around it.
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("account-retention__form", html, StringComparison.Ordinal);
    }

    private static async Task<string> GetHtmxAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(HtmxHeaders.Request, HtmxHeaders.RequestValue);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> PaidClientAsync()
    {
        // Own account per case: the policy is account-wide, so a shared user would leak
        // one case's window into the next.
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task SetPolicyAsync(HttpClient client, int days, bool noExtend = false)
    {
        var response = await client.PutAsJsonAsync("/api/account/retention",
            new UpdateRetentionPolicyRequest { RetentionDays = days, NoExtend = noExtend });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<DateTime?> ExpiryOfAsync(Guid delivery)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery)).ExpiresAt;
    }

    private async Task<Guid> SeedDeliveryAsync(Guid senderId, DateTime expiresAt, bool legalHold = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            TotalBytes = TestFileSize,
            ChunkCount = 1,
            ShareCode = "ret" + Guid.NewGuid().ToString("N")[..8],
            Status = DeliveryStatus.Active,
            SenderId = senderId,
            MaxDownloads = 5,
            ExpiresAt = expiresAt,
            LegalHoldAt = legalHold ? DateTime.UtcNow : null,
            LegalHoldReason = legalHold ? "CASE-RET" : null,
            LegalHoldById = legalHold ? senderId : null,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private async Task<Guid> UploadFileAsync(HttpClient client, double expiryHours)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = expiryHours,
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        return start.DeliveryId;
    }
}
