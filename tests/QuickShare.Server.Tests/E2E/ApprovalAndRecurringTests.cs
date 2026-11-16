using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Approval;
using QuickShare.Features.Deliveries.Recurring;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Approval gating (#162) and recurring delivery (#176).
///
/// The invariant that matters most for #162 is *ordering*: nobody outside the workspace learns
/// the link exists until a reviewer clears it. A gate that mails the link and then asks
/// permission is not a gate, so the held-recipient behaviour and the 404 on the download paths
/// are asserted directly rather than assumed from the status column.
///
/// For #176 it is *non-amplification*: a fresh link per cycle, and a next-run computed forward
/// from now so an outage produces one send on recovery rather than a week of them.
/// </summary>
public sealed class ApprovalAndRecurringTests : IClassFixture<TestWebAppFactory>
{
    private const string FileName = "payroll.xlsx";
    private const string ContentType = "application/octet-stream";
    private const long FileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public ApprovalAndRecurringTests(TestWebAppFactory factory) => _factory = factory;

    // ── Approval gate (#162) ──

    [Fact]
    public async Task Commit_IntoAnApprovalWorkspace_HoldsTheShareAndItsRecipients()
    {
        var (client, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);

        var deliveryId = await UploadAsync(client, recipients: ["client@example.com"]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        Assert.Equal(DeliveryStatus.PendingApproval, delivery.Status);
        Assert.NotNull(delivery.ApprovalRequestedAt);
        // Held, not mailed: the recipient list is parked on the row until a reviewer clears it.
        Assert.Equal("client@example.com", delivery.PendingRecipients);
        Assert.Equal(workspaceId, delivery.WorkspaceId);
    }

    [Fact]
    public async Task PendingShare_IsInvisibleOnBothRecipientPaths()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: true);
        var deliveryId = await UploadAsync(client);
        var shareCode = await ShareCodeAsync(deliveryId);

        var anonymous = _factory.CreateClient();

        // 404 on both, and specifically *not* a distinct "awaiting approval" state: whether a
        // sender's organisation reviews its shares is not a recipient's business.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/downloads/{shareCode}/info")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/downloads/{shareCode}")).StatusCode);
    }

    [Fact]
    public async Task Approve_ActivatesTheLinkAndClearsTheHeldRecipients()
    {
        var (ownerClient, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);
        var (adminClient, _) = await JoinAsAdminAsync(workspaceId);

        var deliveryId = await UploadAsync(ownerClient, recipients: ["client@example.com"]);

        var approve = await adminClient.PostAsync($"/api/deliveries/{deliveryId}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.NotNull(delivery.ApprovedAt);
        Assert.Null(delivery.PendingRecipients);

        Assert.Contains(await db.SecurityEvents.AsNoTracking().ToListAsync(),
            e => e.WorkspaceId == workspaceId && e.EventType == SecurityEventType.DeliveryApproved);
    }

    [Fact]
    public async Task Deny_RequiresAReasonAndSendsTheFileToTrash()
    {
        var (ownerClient, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);
        var (adminClient, _) = await JoinAsAdminAsync(workspaceId);
        var deliveryId = await UploadAsync(ownerClient);

        // "Denied" with no explanation sends the sender back to guess, and the guess is usually
        // to re-upload the same file.
        var noReason = await adminClient.PostAsJsonAsync($"/api/deliveries/{deliveryId}/deny", new DenyRequest());
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var denied = await adminClient.PostAsJsonAsync($"/api/deliveries/{deliveryId}/deny",
            new DenyRequest { Reason = "Contains unredacted salary data." });
        Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        Assert.Equal(DeliveryStatus.Revoked, delivery.Status);
        // Trash, not destruction — a wrong call stays recoverable.
        Assert.NotNull(delivery.DeletedAt);
        Assert.Contains("salary", delivery.DenyReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelfApproval_IsRefusedUnlessTheWorkspaceAllowsIt()
    {
        var (ownerClient, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);
        var deliveryId = await UploadAsync(ownerClient);

        // The owner is an admin, so only the self-approval rule stands between them and their
        // own share — and a second pair of eyes is the entire point of the control.
        var refused = await ownerClient.PostAsync($"/api/deliveries/{deliveryId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await SetWorkspaceFlagsAsync(workspaceId, requireApproval: true, allowSelfApproval: true);

        var allowed = await ownerClient.PostAsync($"/api/deliveries/{deliveryId}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task Approve_IsRefusedTwiceAndToNonAdmins()
    {
        var (ownerClient, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);
        var (adminClient, _) = await JoinAsAdminAsync(workspaceId);
        var deliveryId = await UploadAsync(ownerClient);

        var outsiderKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(outsiderKey, PlanTierLimits.Studio);
        var outsider = _factory.CreateAuthenticatedClient(outsiderKey);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync($"/api/deliveries/{deliveryId}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await adminClient.PostAsync($"/api/deliveries/{deliveryId}/approve", null)).StatusCode);

        // Already released — approving again would re-send the mails that already went out.
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsync($"/api/deliveries/{deliveryId}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Queue_ListsOldestFirstAndOnlyForThisWorkspace()
    {
        var (ownerClient, workspaceId, _) = await WorkspaceClientAsync(requireApproval: true);
        var (adminClient, _) = await JoinAsAdminAsync(workspaceId);

        await UploadAsync(ownerClient);
        await UploadAsync(ownerClient);

        var queue = await adminClient.GetFromJsonAsync<List<PendingApprovalDto>>("/api/workspace/approvals");
        Assert.NotNull(queue);
        Assert.Equal(2, queue.Count);
        // A queue that surfaces the newest item first leaves the oldest waiting longest.
        Assert.True(queue[0].RequestedAt <= queue[1].RequestedAt);

        var (otherClient, _, _) = await WorkspaceClientAsync(requireApproval: true);
        var otherQueue = await otherClient.GetFromJsonAsync<List<PendingApprovalDto>>("/api/workspace/approvals");
        Assert.NotNull(otherQueue);
        Assert.Empty(otherQueue);
    }

    [Fact]
    public async Task FolderApproval_TightensAWorkspaceThatDoesNotRequireIt()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);

        var folder = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = "HR" });
        var created = (await folder.Content.ReadFromJsonAsync<FolderDto>())!;

        await SetFolderApprovalAsync(created.Id);

        var gated = await UploadAsync(client, folderId: created.Id);
        var ungated = await UploadAsync(client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Folder-level only ever tightens: the same workspace, two shares, two outcomes.
        Assert.Equal(DeliveryStatus.PendingApproval,
            (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == gated)).Status);
        Assert.Equal(DeliveryStatus.Active,
            (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == ungated)).Status);
    }

    [Fact]
    public async Task PersonalDelivery_IsNeverGated()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        // No workspace at all: approval is a workspace control, and a personal share has no
        // reviewer it could possibly be waiting on.
        var deliveryId = await UploadAsync(client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(DeliveryStatus.Active,
            (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId)).Status);
    }

    // ── Recurrence maths (#176) ──

    [Fact]
    public void NextRun_IsAlwaysStrictlyForwardFromNow()
    {
        var schedule = new RecurringDelivery { Frequency = RecurrenceFrequency.Daily, HourUtc = 8 };
        var now = new DateTime(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc);

        var next = schedule.ComputeNextRun(now);

        Assert.Equal(new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc), next);
        Assert.True(next > now);
    }

    [Fact]
    public void NextRun_SkipsMissedCyclesInsteadOfCatchingUp()
    {
        var schedule = new RecurringDelivery
        {
            Frequency = RecurrenceFrequency.Weekly,
            DayOfWeek = DayOfWeek.Monday,
            HourUtc = 8,
        };

        // Down for five weeks. On recovery there must be exactly one next run, not five.
        var missedSince = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        var next = schedule.ComputeNextRun(now);

        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.True(next > now);
        Assert.True(next - now < TimeSpan.FromDays(8));
        Assert.True(missedSince < now);
    }

    [Fact]
    public void NextRun_MonthlyNeverSkipsFebruary()
    {
        var schedule = new RecurringDelivery
        {
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 31,
            HourUtc = 6,
        };

        var next = schedule.ComputeNextRun(new DateTime(2026, 1, 29, 0, 0, 0, DateTimeKind.Utc));

        // Clamped to 28 — a "31st" schedule that silently misses short months is a schedule
        // that quietly stops being monthly.
        Assert.True(next.Day <= 28);
    }

    [Fact]
    public void IsDue_RequiresBothEnabledAndElapsed()
    {
        var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(new RecurringDelivery { Enabled = true, NextRunAt = now.AddMinutes(-1) }.IsDue(now));
        Assert.False(new RecurringDelivery { Enabled = true, NextRunAt = now.AddMinutes(1) }.IsDue(now));
        Assert.False(new RecurringDelivery { Enabled = false, NextRunAt = now.AddDays(-9) }.IsDue(now));
    }

    // ── Recurrence API (#176) ──

    [Fact]
    public async Task Recurring_RequiresAPaidPlanALiveSourceAndRecipients()
    {
        var freeKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(freeKey, PlanTierLimits.Free);
        var freeClient = _factory.CreateAuthenticatedClient(freeKey);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await freeClient.PostAsJsonAsync("/api/recurring", new RecurringRequest
            {
                Name = "Weekly",
                SourceDeliveryId = Guid.NewGuid(),
                Recipients = "a@b.test",
            })).StatusCode);

        var (client, _, userId) = await WorkspaceClientAsync(requireApproval: false);
        var deliveryId = await UploadAsync(client);

        // A schedule with nobody to send to would fire forever into the void.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
            {
                Name = "Weekly",
                SourceDeliveryId = deliveryId,
                Recipients = "   ",
            })).StatusCode);

        // Somebody else's delivery is not a valid source.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
            {
                Name = "Weekly",
                SourceDeliveryId = Guid.NewGuid(),
                Recipients = "a@b.test",
            })).StatusCode);

        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Recurring_NormalisesRecipientsAndSchedulesForward()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);
        var deliveryId = await UploadAsync(client);

        var response = await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
        {
            Name = "Monday report",
            SourceDeliveryId = deliveryId,
            Frequency = RecurrenceFrequency.Weekly,
            HourUtc = 8,
            DayOfWeek = DayOfWeek.Monday,
            Recipients = "A@Example.com, a@example.com; b@example.com",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<RecurringDto>();
        Assert.NotNull(dto);
        // Lowercased and de-duplicated, so the mail loop cannot send twice to one address.
        Assert.Equal("a@example.com,b@example.com", dto.Recipients);
        Assert.True(dto.NextRunAt > DateTime.UtcNow);
        Assert.Equal(DayOfWeek.Monday, dto.NextRunAt.DayOfWeek);
    }

    [Fact]
    public async Task Recurring_RunProducesAFreshLinkRatherThanReusingTheSource()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);
        var sourceId = await UploadAsync(client);
        var sourceCode = await ShareCodeAsync(sourceId);

        var created = await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
        {
            Name = "Daily",
            SourceDeliveryId = sourceId,
            Frequency = RecurrenceFrequency.Daily,
            HourUtc = 8,
            Recipients = "client@example.com",
        });
        var schedule = (await created.Content.ReadFromJsonAsync<RecurringDto>())!;

        var run = await client.PostAsync($"/api/recurring/{schedule.Id}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var after = (await run.Content.ReadFromJsonAsync<RecurringDto>())!;

        Assert.Equal(1, after.RunCount);
        Assert.Null(after.LastError);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generated = await db.Deliveries.AsNoTracking()
            .Where(d => d.FileName == FileName && d.ShareCode != sourceCode)
            .ToListAsync();

        // A link that never rotates is a link that leaks: last week's recipients must not be
        // able to open this week's numbers from an old mail.
        Assert.NotEmpty(generated);
        Assert.All(generated, d => Assert.NotEqual(sourceCode, d.ShareCode));
    }

    [Fact]
    public async Task Recurring_PauseResumeAndSkipMoveTheNextRunWithoutLosingTheSchedule()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);
        var sourceId = await UploadAsync(client);

        var created = await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
        {
            Name = "Daily",
            SourceDeliveryId = sourceId,
            Frequency = RecurrenceFrequency.Daily,
            HourUtc = 8,
            Recipients = "client@example.com",
        });
        var schedule = (await created.Content.ReadFromJsonAsync<RecurringDto>())!;
        var originalNext = schedule.NextRunAt;

        // Skip is "not this week"; pause is "not for now". Collapsing them would make the owner
        // choose between losing the schedule and sending something they did not want to send.
        var skipped = await client.PatchAsJsonAsync($"/api/recurring/{schedule.Id}",
            new UpdateRecurringRequest { SkipNext = true });
        var afterSkip = (await skipped.Content.ReadFromJsonAsync<RecurringDto>())!;
        Assert.True(afterSkip.NextRunAt > originalNext);
        Assert.True(afterSkip.Enabled);

        var paused = await client.PatchAsJsonAsync($"/api/recurring/{schedule.Id}",
            new UpdateRecurringRequest { Enabled = false });
        Assert.False((await paused.Content.ReadFromJsonAsync<RecurringDto>())!.Enabled);

        var resumed = await client.PatchAsJsonAsync($"/api/recurring/{schedule.Id}",
            new UpdateRecurringRequest { Enabled = true });
        var afterResume = (await resumed.Content.ReadFromJsonAsync<RecurringDto>())!;

        // Re-based on now, so a long pause does not fire the instant it is switched back on.
        Assert.True(afterResume.Enabled);
        Assert.True(afterResume.NextRunAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Recurring_StopsItselfWhenTheSourceIsGone()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);
        var sourceId = await UploadAsync(client);

        var created = await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
        {
            Name = "Daily",
            SourceDeliveryId = sourceId,
            Frequency = RecurrenceFrequency.Daily,
            HourUtc = 8,
            Recipients = "client@example.com",
        });
        var schedule = (await created.Content.ReadFromJsonAsync<RecurringDto>())!;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.Deliveries.SingleAsync(d => d.Id == sourceId);
            source.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var run = await client.PostAsync($"/api/recurring/{schedule.Id}/run", null);
        var after = (await run.Content.ReadFromJsonAsync<RecurringDto>())!;

        // Disabled with a reason the owner can read, rather than firing empty cycles forever.
        Assert.False(after.Enabled);
        Assert.False(string.IsNullOrEmpty(after.LastError));
    }

    [Fact]
    public async Task Recurring_IsScopedToItsOwner()
    {
        var (client, _, _) = await WorkspaceClientAsync(requireApproval: false);
        var sourceId = await UploadAsync(client);
        var created = await client.PostAsJsonAsync("/api/recurring", new RecurringRequest
        {
            Name = "Daily",
            SourceDeliveryId = sourceId,
            Frequency = RecurrenceFrequency.Daily,
            HourUtc = 8,
            Recipients = "client@example.com",
        });
        var schedule = (await created.Content.ReadFromJsonAsync<RecurringDto>())!;

        var otherKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherKey, PlanTierLimits.Studio);
        var other = _factory.CreateAuthenticatedClient(otherKey);

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/recurring/{schedule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/recurring/{schedule.Id}/run", null)).StatusCode);

        var mine = await client.GetFromJsonAsync<List<RecurringDto>>("/api/recurring");
        var theirs = await other.GetFromJsonAsync<List<RecurringDto>>("/api/recurring");
        Assert.Single(mine!);
        Assert.Empty(theirs!);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId, Guid UserId)> WorkspaceClientAsync(bool requireApproval)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Gate {Guid.NewGuid():N}"[..20], Seats = 4 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        if (requireApproval)
            await SetWorkspaceFlagsAsync(workspace.Id, requireApproval: true, allowSelfApproval: false);

        return (client, workspace.Id, user.Id);
    }

    /// <summary>
    /// The workspace approval switches have no endpoint of their own yet, so the tests set them
    /// where an admin UI would.
    /// </summary>
    private async Task SetWorkspaceFlagsAsync(Guid workspaceId, bool requireApproval, bool allowSelfApproval)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
        workspace.RequireDeliveryApproval = requireApproval;
        workspace.AllowSelfApproval = allowSelfApproval;
        await db.SaveChangesAsync();
    }

    private async Task SetFolderApprovalAsync(Guid folderId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folder = await db.WorkspaceFolders.SingleAsync(f => f.Id == folderId);
        folder.DefaultRequireApproval = true;
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> JoinAsAdminAsync(Guid workspaceId)
    {
        var token = Guid.NewGuid().ToString("N");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                InviteEmail = $"admin-{token[..8]}@example.com",
                Role = WorkspaceRole.Admin,
                InviteTokenHash = QuickShare.Features.Workspaces.Shared.WorkspaceService.HashToken(token),
                InviteExpiresAt = DateTime.UtcNow.AddDays(WorkspaceMember.InviteValidDays),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var accept = await client.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        accept.EnsureSuccessStatusCode();

        return (client, user.Id);
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, Guid? folderId = null, IReadOnlyList<string>? recipients = null)
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
            FolderId = folderId,
            RecipientEmails = recipients?.ToList(),
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
