using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The four screens that #162, #176, #180 and #289 shipped without: an approval queue, a schedule
/// list, a channel config and a comment thread.
///
/// The audit that prompted these found the same failure each time — an endpoint nobody could
/// reach. So these tests assert the *page* carries the data, not that a handler returns it: a
/// render that omits the queue is the exact defect being fixed, and a 200 alone would not catch
/// it. The HTMX fragment is what gets asserted because that is the half that runs the queries.
/// </summary>
public sealed class B2BUiTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "quarterly.pdf";
    private const string ContentType = "application/pdf";
    private const long FileSize = 2048;
    private const string HtmxHeader = "HX-Request";

    private readonly TestWebAppFactory _factory;

    public B2BUiTests(TestWebAppFactory factory) => _factory = factory;

    // ── #162 approval queue ──

    [Fact]
    public async Task ApprovalsPage_ListsPendingDelivery()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await RequireApprovalAsync(workspaceId);
        await UploadAsync(client);

        var html = await FragmentAsync(client, RoutePaths.AccountWorkspaceApprovals);

        Assert.Contains(FileName, html, StringComparison.Ordinal);
        Assert.Contains("approval.approve", html, StringComparison.Ordinal);
        Assert.Contains("approval.deny", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalsPage_TogglesPolicy()
    {
        // The switches are the point of the screen: before it, these two columns could only be
        // set by editing the database.
        var (client, workspaceId) = await WorkspaceClientAsync();

        var response = await client.PatchAsJsonAsync("/api/workspace", new UpdateWorkspaceRequest
        {
            RequireDeliveryApproval = true,
            AllowSelfApproval = false,
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == workspaceId);

        Assert.True(workspace.RequireDeliveryApproval);
        Assert.False(workspace.AllowSelfApproval);
        Assert.False(string.IsNullOrEmpty(workspace.Name));
    }

    [Fact]
    public async Task ApprovalsPage_RefusesNonAdmin()
    {
        // No workspace at all — the page must degrade to the admin-only card, not throw.
        var client = await PlainClientAsync();

        var html = await FragmentAsync(client, RoutePaths.AccountWorkspaceApprovals);

        Assert.DoesNotContain("approval.approve", html, StringComparison.Ordinal);
    }

    // ── #180 channel config ──

    [Fact]
    public async Task ChannelPage_ShowsStoredHookWithoutLeakingUrl()
    {
        const string Webhook = "https://hooks.slack.com/services/T/B/xoxb-secret-value";
        var (client, _) = await WorkspaceClientAsync();

        var save = await client.PutAsJsonAsync("/api/workspace/channel", new
        {
            kind = ChannelKind.Slack,
            webhookUrl = Webhook,
            enabled = true,
            notifyOnDelivery = true,
            notifyOnComment = true,
        });
        save.EnsureSuccessStatusCode();

        var html = await FragmentAsync(client, RoutePaths.AccountWorkspaceChannel);

        Assert.Contains("channel.save", html, StringComparison.Ordinal);
        Assert.Contains("channel.remove", html, StringComparison.Ordinal);
        // The credential must never come back down: the field says one is stored, not which.
        Assert.DoesNotContain("xoxb-secret-value", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChannelPage_RendersWithNoHookConfigured()
    {
        var (client, _) = await WorkspaceClientAsync();

        var html = await FragmentAsync(client, RoutePaths.AccountWorkspaceChannel);

        Assert.Contains("channel.save", html, StringComparison.Ordinal);
        // Nothing to remove yet, so no remove button and no health card.
        Assert.DoesNotContain("channel.remove", html, StringComparison.Ordinal);
    }

    // ── #176 schedule list ──

    [Fact]
    public async Task RecurringPage_ListsSchedule()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client);

        var created = await client.PostAsJsonAsync("/api/recurring", new
        {
            name = "Weekly report",
            sourceDeliveryId = deliveryId,
            frequency = RecurrenceFrequency.Weekly,
            hourUtc = 8,
            dayOfWeek = (int)DayOfWeek.Monday,
            recipients = "ops@example.com",
        });
        created.EnsureSuccessStatusCode();

        var html = await FragmentAsync(client, RoutePaths.AccountRecurring);

        Assert.Contains("Weekly report", html, StringComparison.Ordinal);
        Assert.Contains("ops@example.com", html, StringComparison.Ordinal);
        Assert.Contains("recurring.pause", html, StringComparison.Ordinal);
        Assert.Contains("recurring.skip", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecurringPage_OffersLiveDeliveryAsSource()
    {
        // The create form is only usable if it can name something to repeat.
        var (client, _) = await WorkspaceClientAsync();
        await UploadAsync(client);

        var html = await FragmentAsync(client, RoutePaths.AccountRecurring);

        Assert.Contains("recurring.create", html, StringComparison.Ordinal);
        Assert.Contains(FileName, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecurringPage_RefusesFreePlan()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await FragmentAsync(client, RoutePaths.AccountRecurring);

        Assert.DoesNotContain("recurring.create", html, StringComparison.Ordinal);
    }

    // ── #289 comment thread ──

    [Fact]
    public async Task DownloadPage_RendersThreadServerSide()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        var posted = await client.PostAsJsonAsync(
            $"/api/deliveries/{shareCode}/comments", new { body = "Looks right to me." });
        posted.EnsureSuccessStatusCode();

        var html = await client.GetStringAsync($"/d/{shareCode}");

        // Server-rendered, not fetched: the conversation survives with scripting off.
        Assert.Contains("Looks right to me.", html, StringComparison.Ordinal);
        Assert.Contains("dl-comments", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadPage_OmitsThreadWhenCommentsOff()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: false);
        var shareCode = await ShareCodeAsync(deliveryId);

        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("id=\"dl-comments\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadPage_HidesPostingFormForAnonymousReaderWhenPolicyIsMembers()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);
        await SetPolicyAsync(workspaceId, CommentPolicy.MembersOnly);

        var anonymous = _factory.CreateClient();
        var html = await anonymous.GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("comment.post", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private static async Task<string> FragmentAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(HtmxHeader, "true");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpClient> PlainClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        return _factory.CreateAuthenticatedClient(providerKey);
    }

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"UI {Guid.NewGuid():N}"[..16], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private async Task RequireApprovalAsync(Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
        workspace.RequireDeliveryApproval = true;
        await db.SaveChangesAsync();
    }

    private async Task SetPolicyAsync(Guid workspaceId, string policy)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
        workspace.CommentPolicy = policy;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, bool commentsEnabled = false)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
        });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartR2Response>())!;

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = 24,
            CommentsEnabled = commentsEnabled,
        });
        commit.EnsureSuccessStatusCode();

        return start.DeliveryId;
    }

    private async Task<string> ShareCodeAsync(Guid deliveryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == deliveryId)
            .Select(d => d.ShareCode)
            .SingleAsync();
    }
}
