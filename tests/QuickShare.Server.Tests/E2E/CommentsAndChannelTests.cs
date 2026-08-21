using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Comments;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Channels;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Delivery comments (#289) and Slack/Teams posting (#180), plus the end-to-end residency
/// assertion that should have existed when `#119` shipped.
///
/// For comments the invariants are about *closing*: three independent gates — the sender's
/// switch, the workspace policy and privacy mode — any of which can shut a thread, none of which
/// can open one. For the channel hook it is that the webhook URL behaves like the credential it
/// is, and that a chat outage can never turn into a failed upload.
/// </summary>
public sealed class CommentsAndChannelTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "contract.pdf";
    private const string ContentType = "application/pdf";
    private const long FileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public CommentsAndChannelTests(TestWebAppFactory factory) => _factory = factory;

    // ── The #119 regression this batch found ──

    [Fact]
    public async Task Upload_RecordsTheStorageRegionItWasPinnedTo()
    {
        var (client, _) = await WorkspaceClientAsync();

        var deliveryId = await UploadAsync(client, region: "eu");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        // The key routes the bytes; this column is what the recipient badge and every copy path
        // read. It was silently null when #119 shipped, because no test crossed the commit path.
        Assert.Equal("eu", delivery.StorageRegion);
        Assert.Equal("eu", StorageRegions.FromKey(delivery.StoragePath));
    }

    [Fact]
    public async Task Upload_WithoutARegionRecordsNoneAndStaysInTheDefaultBucket()
    {
        var (client, _) = await WorkspaceClientAsync();

        var deliveryId = await UploadAsync(client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        Assert.Null(delivery.StorageRegion);
        Assert.Null(StorageRegions.FromKey(delivery.StoragePath));
    }

    // ── Comments: the three gates (#289) ──

    [Fact]
    public async Task Comments_AreClosedUnlessTheSenderOpensThem()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client));

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        // A share is one-way unless the sender says otherwise.
        Assert.NotNull(thread);
        Assert.False(thread.CanPost);
        Assert.Empty(thread.Comments);

        var post = await anonymous.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "hello", AuthorName = "Client" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Comments_PrivacyModeOverridesTheSenderSwitch()
    {
        var (client, _) = await WorkspaceClientAsync();
        var deliveryId = await UploadAsync(client, commentsEnabled: true, privacyMode: true);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        // A share that deliberately keeps no download record must not accumulate a public
        // thread instead.
        Assert.False(delivery.CommentsEnabled);
    }

    [Fact]
    public async Task Comments_WorkspacePolicyCanCloseAThreadTheSenderOpened()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));

        var anonymous = _factory.CreateClient();
        Assert.True((await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments"))!.CanPost);

        await SetPolicyAsync(workspaceId, CommentPolicy.Disabled);

        var closed = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");
        Assert.NotNull(closed);
        Assert.False(closed.CanPost);

        var post = await anonymous.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "hello", AuthorName = "Client" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Comments_MembersOnlyPolicyLetsRecipientsReadButNotPost()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));
        await SetPolicyAsync(workspaceId, CommentPolicy.MembersOnly);

        var anonymous = _factory.CreateClient();
        var thread = await anonymous.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");

        // Reading is not the thing being restricted — posting is.
        Assert.NotNull(thread);
        Assert.True(thread.Comments.Count == 0);
        Assert.False(thread.CanPost);

        // The sender is a member of their own workspace, so they can still talk.
        var mine = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Internal note" });
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
    }

    // ── Comments: posting (#289) ──

    [Fact]
    public async Task Comment_FromAnAnonymousRecipientNeedsANameAndIsMarkedAsSuch()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));
        var anonymous = _factory.CreateClient();

        var nameless = await anonymous.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Is section 3 final?" });
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);

        var posted = await anonymous.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Is section 3 final?", AuthorName = "Client" });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var dto = await posted.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(dto);
        Assert.Equal("Client", dto.AuthorName);
        // A supplied name is a claim, not an identity — the thread says which authors are real
        // accounts so a reader is not invited to assume.
        Assert.False(dto.IsTeamMember);
    }

    [Fact]
    public async Task Comment_FromTheSenderIsMarkedAsATeamMember()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));

        var posted = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Yes, section 3 is final." });

        var dto = await posted.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(dto);
        Assert.True(dto.IsTeamMember);
        // The display name comes from the account, not from anything the client sent.
        Assert.Equal(TestWebAppFactory.TestUserName, dto.AuthorName);
    }

    [Fact]
    public async Task Comment_RepliesAreOneLevelDeep()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));

        var root = (await (await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Question" })).Content.ReadFromJsonAsync<CommentDto>())!;

        var reply = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Answer", ParentId = root.Id });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var replyDto = (await reply.Content.ReadFromJsonAsync<CommentDto>())!;

        // Deeper threads need collapsing UI nobody has asked for.
        var nested = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Follow-up", ParentId = replyDto.Id });
        Assert.Equal(HttpStatusCode.BadRequest, nested.StatusCode);
    }

    [Fact]
    public async Task Comment_DeleteKeepsThePlaceAndRemovesTheWords()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));

        var root = (await (await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Sensitive detail" })).Content.ReadFromJsonAsync<CommentDto>())!;
        await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Reply to it", ParentId = root.Id });

        var deleted = await client.DeleteAsync($"/api/deliveries/{shareCode}/comments/{root.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var thread = await client.GetFromJsonAsync<CommentThreadDto>($"/api/deliveries/{shareCode}/comments");
        Assert.NotNull(thread);
        var tombstone = thread.Comments.Single(c => c.Id == root.Id);

        // The row stays so the reply keeps its context; the text does not, because a delete
        // that leaves the words readable was not one.
        Assert.True(tombstone.IsDeleted);
        Assert.Equal(string.Empty, tombstone.Body);
        Assert.Equal(2, thread.Comments.Count);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(string.Empty,
            (await db.DeliveryComments.AsNoTracking().SingleAsync(c => c.Id == root.Id)).Body);
    }

    [Fact]
    public async Task Comment_DeleteIsRefusedToAStranger()
    {
        var (client, _) = await WorkspaceClientAsync();
        var shareCode = await ShareCodeAsync(await UploadAsync(client, commentsEnabled: true));
        var root = (await (await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Mine" })).Content.ReadFromJsonAsync<CommentDto>())!;

        var otherKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherKey, PlanTierLimits.Studio);
        var other = _factory.CreateAuthenticatedClient(otherKey);

        var refused = await other.DeleteAsync($"/api/deliveries/{shareCode}/comments/{root.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Comments_OnAnUnknownShareCodeAre404()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync("/api/deliveries/does-not-exist/comments")).StatusCode);
    }

    // ── Channel hook (#180) ──

    [Fact]
    public async Task Channel_RefusesPlaintextAndUnknownPlatforms()
    {
        var (client, _) = await WorkspaceClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
            {
                Kind = ChannelKind.Slack,
                WebhookUrl = "http://hooks.slack.com/services/x",
            })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
            {
                Kind = "irc",
                WebhookUrl = "https://hooks.slack.com/services/x",
            })).StatusCode);

        // A first save with no URL would create a hook that can never post.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
            {
                Kind = ChannelKind.Slack,
            })).StatusCode);
    }

    [Fact]
    public async Task Channel_WebhookUrlIsWriteOnlyAndBlankKeepsIt()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        const string Url = "https://hooks.slack.com/services/T000/B000/secrettoken";

        var saved = await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
        {
            Kind = ChannelKind.Slack,
            WebhookUrl = Url,
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        // Anyone holding the URL can post into that channel, so it is a credential.
        var body = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secrettoken", body, StringComparison.Ordinal);

        var read = await client.GetFromJsonAsync<ChannelHookDto>("/api/workspace/channel");
        Assert.NotNull(read);
        Assert.True(read.HasUrl);

        await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
        {
            Kind = ChannelKind.Teams,
            NotifyOnComment = false,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hook = await db.WorkspaceChannelHooks.AsNoTracking().SingleAsync(h => h.WorkspaceId == workspaceId);
        Assert.Equal(Url, hook.WebhookUrl);
        Assert.Equal(ChannelKind.Teams, hook.Kind);
        Assert.False(hook.NotifyOnComment);
    }

    [Fact]
    public async Task Channel_AuditRecordsTheHostNotTheCredential()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
        {
            Kind = ChannelKind.Slack,
            WebhookUrl = "https://hooks.slack.com/services/T000/B000/secrettoken",
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId && e.EventType == SecurityEventType.ChannelHookChanged)
            .ToListAsync();

        var entry = Assert.Single(audit);
        Assert.Contains("hooks.slack.com", entry.MetadataJson!, StringComparison.Ordinal);
        // An audit trail that records the credential is a second place the credential lives.
        Assert.DoesNotContain("secrettoken", entry.MetadataJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Channel_AnUnreachableWebhookNeverBreaksTheShareOrTheComment()
    {
        var (client, _) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
        {
            Kind = ChannelKind.Slack,
            // Reserved TEST-NET-1: guaranteed not to answer.
            WebhookUrl = "https://192.0.2.1/services/x",
        });

        // A chat outage must never surface as a failed upload, or a refused comment.
        var deliveryId = await UploadAsync(client, commentsEnabled: true);
        var shareCode = await ShareCodeAsync(deliveryId);

        var comment = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/comments",
            new PostCommentRequest { Body = "Still works" });

        Assert.Equal(HttpStatusCode.OK, comment.StatusCode);
    }

    [Fact]
    public async Task Channel_IsAdminOnly()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var seatless = _factory.CreateAuthenticatedClient(providerKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await seatless.GetAsync("/api/workspace/channel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seatless.DeleteAsync("/api/workspace/channel")).StatusCode);
    }

    [Fact]
    public async Task Channel_DeleteForgetsTheWebhook()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/channel", new ChannelHookRequest
        {
            Kind = ChannelKind.Slack,
            WebhookUrl = "https://hooks.slack.com/services/x",
        });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/workspace/channel")).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkspaceChannelHooks.AsNoTracking().AnyAsync(h => h.WorkspaceId == workspaceId));
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Talk {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private async Task SetPolicyAsync(Guid workspaceId, string policy)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
        workspace.CommentPolicy = policy;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, bool commentsEnabled = false, bool privacyMode = false, string? region = null)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
            StorageRegion = region,
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
            PrivacyMode = privacyMode,
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
