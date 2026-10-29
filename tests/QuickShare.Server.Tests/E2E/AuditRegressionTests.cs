using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Comments;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Regressions for defects found by auditing the tier-6 batches, not by anybody using the
/// product. Each case exists because something was written, documented as working, and never
/// actually reached — so each one asserts the *reaching*, not the shape of the code.
///
/// The pattern behind all of them: a unit test proves a function computes the right answer, and
/// says nothing about whether anything calls it. These are the wiring tests.
/// </summary>
public sealed class AuditRegressionTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "audit.pdf";
    private const string ContentType = "application/pdf";
    private const long FileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public AuditRegressionTests(TestWebAppFactory factory) => _factory = factory;

    // ── Comments must obey the delivery's own gates (found: they bypassed all of them) ──

    [Fact]
    public async Task Comments_AreClosedOnAnExpiredDelivery()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        await MutateAsync(deliveryId, d => d.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        // A share nobody can open should not still be hosting a readable conversation.
        Assert.NotNull(thread);
        Assert.False(thread.CanPost);
        Assert.Empty(thread.Comments);
    }

    [Fact]
    public async Task Comments_AreClosedOnARevokedDelivery()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        var before = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Before revocation" });
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await MutateAsync(deliveryId, d => d.Status = DeliveryStatus.Revoked);

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        // Revoking a link has to take the discussion with it, or revocation is cosmetic.
        Assert.NotNull(thread);
        Assert.Empty(thread.Comments);
    }

    [Fact]
    public async Task Comments_AreClosedOnAShareHeldForApproval()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        await MutateAsync(deliveryId, d => d.Status = DeliveryStatus.PendingApproval);
        Assert.NotEqual(Guid.Empty, workspaceId);

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        // #162 went to some trouble to make a held share invisible; its thread is part of that.
        Assert.NotNull(thread);
        Assert.False(thread.CanPost);
    }

    [Fact]
    public async Task Comments_AreClosedToACallerTheScopeRulesRefuse()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        var posted = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Team-only discussion" });
        // Guard against a vacuous pass: if the comment never landed, "thread is empty" below
        // would prove nothing about the scope rules.
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        // A CIDR allowlist that excludes the loopback address the test client calls from
        // (issues #142/#166). The file refuses this caller; so must the conversation about it.
        await MutateAsync(deliveryId, d => d.AllowedIpRanges = "203.0.113.0/24");

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        Assert.NotNull(thread);
        Assert.Empty(thread.Comments);
        Assert.False(thread.CanPost);

        var post = await anonymous.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Should not land", AuthorName = "Outsider" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    // ── The channel post on a new delivery (found: the method existed, nothing called it) ──

    [Fact]
    public async Task ChannelHook_IsConsultedWhenADeliveryIsCreated()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/channel",
            new QuickShare.Features.Workspaces.Channels.ChannelHookRequest
            {
                Kind = ChannelKind.Slack,
                // Reserved TEST-NET-1: never answers, so the attempt is recorded as a failure.
                WebhookUrl = "https://192.0.2.1/services/x",
            });

        await UploadAsync(client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hook = await db.WorkspaceChannelHooks.AsNoTracking().SingleAsync(h => h.WorkspaceId == workspaceId);

        // The observable proof that the notifier ran at all: an unreachable endpoint leaves a
        // failure behind. Before this fix the counter stayed at zero, because nothing called it.
        Assert.True(hook.ConsecutiveFailures > 0);
        Assert.False(string.IsNullOrEmpty(hook.LastError));
    }

    [Fact]
    public async Task ChannelHook_IsNotConsultedForAShareHeldForApproval()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/channel",
            new QuickShare.Features.Workspaces.Channels.ChannelHookRequest
            {
                Kind = ChannelKind.Slack,
                WebhookUrl = "https://192.0.2.1/services/x",
            });

        await MutateWorkspaceAsync(workspaceId, w => w.RequireDeliveryApproval = true);
        await UploadAsync(client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hook = await db.WorkspaceChannelHooks.AsNoTracking().SingleAsync(h => h.WorkspaceId == workspaceId);

        // A channel post is how somebody outside first learns a link exists, so it waits for
        // the reviewer exactly as the recipient emails do.
        Assert.Equal(0, hook.ConsecutiveFailures);
    }

    // ── Render coverage for pages the suite never loaded ──

    [Theory]
    [InlineData("/dpa")]
    [InlineData("/soc2")]
    public async Task PublicTrustPage_Renders(string url)
    {
        // Both are anonymous marketing/legal surfaces: a render failure here is a live 500 on a
        // page procurement is reading, and nothing in the suite loaded them before.
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData("/account/workspace")]
    [InlineData("/account/workspace/folders")]
    [InlineData("/account/workspace/seats")]
    [InlineData("/account/workspace/audit")]
    [InlineData("/account/workspace/report")]
    [InlineData("/account/workspace/brand")]
    [InlineData("/account/workspace/backup")]
    [InlineData("/account/workspace/streaming")]
    [InlineData("/account/workspace/approvals")]
    [InlineData("/account/workspace/channel")]
    [InlineData("/account/recurring")]
    [InlineData("/account/invoices")]
    [InlineData("/workspace/join")]
    public async Task WorkspacePage_ShellRenders(string url)
    {
        // Direct requests render the page with its data.
        var (client, _) = await WorkspaceClientAsync();
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/account/workspace")]
    [InlineData("/account/workspace/folders")]
    [InlineData("/account/workspace/seats")]
    [InlineData("/account/workspace/brand")]
    [InlineData("/account/workspace/approvals")]
    [InlineData("/account/workspace/channel")]
    [InlineData("/account/recurring")]
    public async Task WorkspacePage_UnpolyRequestRenders(string url)
    {
        // Targeted requests also need a valid page and authorized data.
        var (client, _) = await WorkspaceClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(UnpolyHeaders.Target, "#main-content");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Audit {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
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

    private async Task MutateAsync(Guid deliveryId, Action<Delivery> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId);
        mutate(delivery);
        await db.SaveChangesAsync();
    }

    private async Task MutateWorkspaceAsync(Guid workspaceId, Action<Workspace> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
        mutate(workspace);
        await db.SaveChangesAsync();
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
