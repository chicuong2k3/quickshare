using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Features.Workspaces.Transfer;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Team workspaces (issue #106) with ownership transfer (#252), the workspace audit lens
/// (#145) and the admin usage report (#300).
///
/// The invariants worth defending, and what each would cost if it broke:
/// seats are held by pending invites (or two admins oversell the last slot); quota pools to
/// the owner (or every seat silently gets a private copy of the plan allowance); the plan a
/// seat grants is borrowed, not taken (or a revoked member loses the plan they pay for);
/// ownership only moves on the successor's word (or someone inherits an invoice); the audit
/// and report lenses are admin-only (or a viewer reads the whole team's activity).
/// </summary>
public sealed class WorkspaceTests : IClassFixture<TestWebAppFactory>
{
    private const string WorkspaceName = "Studio Nam";
    private const int Seats = 3;
    private const string MemberRole = "member";

    private readonly TestWebAppFactory _factory;

    public WorkspaceTests(TestWebAppFactory factory) => _factory = factory;

    // ── Creation ──

    [Fact]
    public async Task Create_OnFreePlan_IsRefused()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Free);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = WorkspaceName, Seats = Seats });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_OnPaidPlan_SeatsTheOwnerAndAudits()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = WorkspaceName, Seats = Seats });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(dto);
        Assert.Equal(WorkspaceName, dto.Name);
        Assert.True(dto.IsOwner);
        // The owner occupies a real seat rather than living outside the member table.
        Assert.Equal(1, dto.SeatsUsed);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.WorkspaceId == dto.Id);
        Assert.Equal(ownerId, seat.UserId);
        Assert.Equal(WorkspaceRole.Owner, seat.Role);

        Assert.Contains(await db.SecurityEvents.AsNoTracking().ToListAsync(),
            e => e.WorkspaceId == dto.Id && e.EventType == SecurityEventType.WorkspaceCreated);
    }

    [Fact]
    public async Task Create_WhenAlreadySeated_IsRefused()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);

        var second = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = "Another", Seats = Seats });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── Seats and invites ──

    [Fact]
    public async Task Invite_HoldsTheSeatWhileItIsOutstanding()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(client, seats: 2);

        var invite = await client.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "first@example.com", Role = MemberRole });
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);

        // Owner seat + one pending invite fills a two-seat workspace. Without the hold, a
        // second admin could hand out the same slot before the first invite is accepted.
        var overflow = await client.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "second@example.com", Role = MemberRole });
        Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);

        var reread = await client.GetFromJsonAsync<WorkspaceDto>("/api/workspace");
        Assert.NotNull(reread);
        Assert.Equal(workspace.Id, reread.Id);
        Assert.Equal(2, reread.SeatsUsed);
        Assert.Contains(reread.Members, m => m.Pending && m.Email == "first@example.com");
    }

    [Fact]
    public async Task Seats_CannotBeCutBelowWhatIsAlreadyClaimed()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client, seats: 3);
        await client.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "held@example.com", Role = MemberRole });

        var shrink = await client.PatchAsJsonAsync("/api/workspace",
            new UpdateWorkspaceRequest { Seats = Workspace.MinSeats });
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);

        // Two seats are held (owner + invite); going to 2 is fine, going below evicts someone.
        var tooFar = await client.PatchAsJsonAsync("/api/workspace",
            new UpdateWorkspaceRequest { Seats = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_BorrowsTheWorkspacePlanAndRemembersTheMemberOwn()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Studio);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, memberId) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "join@example.com");

        var accept = await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Users.AsNoTracking().SingleAsync(u => u.Id == memberId);
        Assert.Equal(PlanTierLimits.Studio, member.Plan);
        // Borrowed, not taken — the revoke path needs to know what to give back.
        Assert.Equal(PlanTierLimits.Free, member.PersonalPlan);
    }

    [Fact]
    public async Task AcceptInvite_AfterExpiry_IsRefusedAndTheSeatStaysUnclaimed()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "late@example.com",
            expiresAt: DateTime.UtcNow.AddDays(-1));

        var accept = await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        Assert.Equal(HttpStatusCode.Gone, accept.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking()
            .SingleAsync(m => m.WorkspaceId == workspace.Id && m.InviteEmail == "late@example.com");
        Assert.Null(seat.UserId);
        Assert.Null(seat.JoinedAt);
    }

    [Fact]
    public async Task Revoke_FreesTheSlotAndGivesTheMemberTheirOwnPlanBack()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Studio);
        var workspace = await CreateWorkspaceAsync(ownerClient, seats: 2);

        var (memberClient, memberId) = await ClientAsync(PlanTierLimits.Pro);
        var token = await SeedInviteAsync(workspace.Id, "revoke@example.com");
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var seatId = await SeatIdAsync(workspace.Id, memberId);
        var revoke = await ownerClient.DeleteAsync($"/api/workspace/members/{seatId}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = await db.Users.AsNoTracking().SingleAsync(u => u.Id == memberId);
        // Restored, not downgraded: the account pays for Pro on its own.
        Assert.Equal(PlanTierLimits.Pro, member.Plan);
        Assert.Null(member.PersonalPlan);

        // The row survives so the audit log keeps its subject, but the slot is free.
        var seat = await db.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.Id == seatId);
        Assert.NotNull(seat.RevokedAt);

        var refill = await ownerClient.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "next@example.com", Role = MemberRole });
        Assert.Equal(HttpStatusCode.OK, refill.StatusCode);
    }

    [Fact]
    public async Task Member_CannotAdministerTheWorkspace()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "plain@example.com");
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var invite = await memberClient.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "nope@example.com", Role = MemberRole });

        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    // ── Pooled quota (the reason seats are worth anything) ──

    [Fact]
    public async Task Quota_IsPooledOntoTheOwnerUsageRow()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, memberId) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "pool@example.com");
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        const long Uploaded = 5L * 1024 * 1024;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quota = new QuotaService(db);
            await quota.RecordAsync(memberId, PlanTierLimits.Pro, Uploaded, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The member's bytes land on the owner's row, and the member has no row at all —
            // a private row would be a second, uncapped allowance.
            var owner = await db.UserUsages.AsNoTracking().SingleAsync(u => u.UserId == ownerId);
            Assert.Equal(Uploaded, owner.TotalBytesUploaded);
            Assert.False(await db.UserUsages.AsNoTracking().AnyAsync(u => u.UserId == memberId));

            var check = await new QuotaService(db).CheckAsync(memberId, PlanTierLimits.Pro, 1, CancellationToken.None);
            Assert.Equal(Uploaded, check.Used);
        }
    }

    // ── Ownership transfer (#252) ──

    [Fact]
    public async Task Transfer_MovesOwnershipOnlyAfterTheSuccessorAccepts()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (successorClient, successorId) = await ClientAsync(PlanTierLimits.Pro);
        var token = await SeedInviteAsync(workspace.Id, "successor@example.com");
        await successorClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var seatId = await SeatIdAsync(workspace.Id, successorId);
        var initiate = await ownerClient.PostAsJsonAsync("/api/workspace/transfer",
            new InitiateTransferRequest { MemberId = seatId });
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);

        // Nomination alone changes nothing — consent to the bill has not happened yet.
        await AssertOwnerAsync(workspace.Id, ownerId);

        var transferToken = await SeedTransferTokenAsync(workspace.Id);
        var accept = await successorClient.PostAsJsonAsync("/api/workspace/transfer/accept",
            new AcceptTransferRequest { Token = transferToken });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        await AssertOwnerAsync(workspace.Id, successorId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seats = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspace.Id && m.RevokedAt == null)
            .ToListAsync();

        Assert.Equal(WorkspaceRole.Owner, seats.Single(m => m.UserId == successorId).Role);
        // Demoted, not evicted — the workspace is never left without an administrator.
        Assert.Equal(WorkspaceRole.Admin, seats.Single(m => m.UserId == ownerId).Role);

        Assert.Contains(await db.SecurityEvents.AsNoTracking().ToListAsync(),
            e => e.WorkspaceId == workspace.Id && e.EventType == SecurityEventType.WorkspaceTransferAccepted);
    }

    [Fact]
    public async Task Transfer_ToSelf_IsRefused()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var seatId = await SeatIdAsync(workspace.Id, ownerId);
        var initiate = await ownerClient.PostAsJsonAsync("/api/workspace/transfer",
            new InitiateTransferRequest { MemberId = seatId });

        // The owner's own seat is not a successor; the guard reads as member-not-found only
        // if the seat lookup misses, so assert on the refusal, not the shape of it.
        Assert.NotEqual(HttpStatusCode.OK, initiate.StatusCode);
        await AssertOwnerAsync(workspace.Id, ownerId);
    }

    [Fact]
    public async Task Transfer_ByAnAdminWhoIsNotTheOwner_IsRefused()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (adminClient, adminId) = await ClientAsync(PlanTierLimits.Pro);
        var token = await SeedInviteAsync(workspace.Id, "admin@example.com", WorkspaceRole.Admin);
        await adminClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var ownerSeat = await SeatIdAsync(workspace.Id, ownerId);
        var attempt = await adminClient.PostAsJsonAsync("/api/workspace/transfer",
            new InitiateTransferRequest { MemberId = ownerSeat });

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
        await AssertOwnerAsync(workspace.Id, ownerId);
        Assert.NotEqual(Guid.Empty, adminId);
    }

    [Fact]
    public async Task AccountDeletion_IsRefusedWhileTheCallerStillOwnsAWorkspace()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);

        var response = await client.PostAsJsonAsync("/api/me/delete",
            new { ConfirmEmail = TestWebAppFactory.TestUserEmail });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Audit lens (#145) ──

    [Fact]
    public async Task Audit_IsAdminOnlyAndFiltersByAction()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        await ownerClient.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "audited@example.com", Role = MemberRole });

        var all = await ownerClient.GetFromJsonAsync<WorkspaceAuditPageDto>("/api/workspace/audit");
        Assert.NotNull(all);
        Assert.Contains(all.Rows, r => r.Action == "workspacememberinvited");
        Assert.Contains(all.Rows, r => r.Action == "workspacecreated");

        var filtered = await ownerClient.GetFromJsonAsync<WorkspaceAuditPageDto>(
            "/api/workspace/audit?action=WorkspaceCreated");
        Assert.NotNull(filtered);
        Assert.All(filtered.Rows, r => Assert.Equal("workspacecreated", r.Action));

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "viewer@example.com", WorkspaceRole.Viewer);
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var refused = await memberClient.GetAsync("/api/workspace/audit");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task AuditPage_DirectAndUnpolyGet_KeepFilterAndAdminGate()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        await ownerClient.PostAsJsonAsync("/api/workspace/invites",
            new InviteMemberRequest { Email = "page-audited@example.com", Role = MemberRole });

        var direct = await ownerClient.GetAsync(RoutePaths.AccountWorkspaceAudit);
        var html = await direct.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Contains("id=\"workspace-audit-app\"", html);
        Assert.Contains("workspacecreated", html);
        Assert.Contains("workspacememberinvited", html);
        Assert.Contains("up-target=\"#workspace-audit-app\"", html);

        using var filteredRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{RoutePaths.AccountWorkspaceAudit}?action=WorkspaceCreated");
        filteredRequest.Headers.Add(UnpolyHeaders.Target, "#workspace-audit-app");
        var filtered = await ownerClient.SendAsync(filteredRequest);
        var filteredHtml = await filtered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Contains("workspacecreated", filteredHtml);
        Assert.DoesNotContain("workspacememberinvited", filteredHtml);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "page-viewer@example.com", WorkspaceRole.Viewer);
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        var denied = await memberClient.GetStringAsync(RoutePaths.AccountWorkspaceAudit);
        Assert.Contains("id=\"workspace-audit-body\"", denied);
        Assert.DoesNotContain("workspacememberinvited", denied);
    }

    [Fact]
    public async Task ApprovalsPage_DirectAndTargetedGet_HideForeignAndViewerQueue()
    {
        const string ownFile = "pending-owner-workspace.pdf";
        const string foreignFile = "pending-other-workspace.pdf";
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var (foreignClient, foreignId) = await ClientAsync(PlanTierLimits.Pro);
        var foreignWorkspace = await CreateWorkspaceAsync(foreignClient);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = ownerId,
                WorkspaceId = workspace.Id,
                FileName = ownFile,
                ShareCode = Guid.CreateVersion7().ToString("N"),
                Status = DeliveryStatus.PendingApproval,
                ApprovalRequestedAt = DateTime.UtcNow,
            });
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = foreignId,
                WorkspaceId = foreignWorkspace.Id,
                FileName = foreignFile,
                ShareCode = Guid.CreateVersion7().ToString("N"),
                Status = DeliveryStatus.PendingApproval,
                ApprovalRequestedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var direct = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceApprovals);
        Assert.Contains("id=\"workspace-approvals-body\"", direct);
        Assert.Contains(ownFile, direct);
        Assert.DoesNotContain(foreignFile, direct);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get,
            RoutePaths.AccountWorkspaceApprovals);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#workspace-approvals-body");
        var targeted = await ownerClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains(ownFile, await targeted.Content.ReadAsStringAsync());

        var token = await SeedInviteAsync(workspace.Id, "approvals-viewer@example.com", WorkspaceRole.Viewer);
        var (viewerClient, _) = await ClientAsync(PlanTierLimits.Free);
        await viewerClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        var viewer = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspaceApprovals);
        Assert.Contains("id=\"workspace-approvals-body\"", viewer);
        Assert.DoesNotContain(ownFile, viewer);
    }

    [Fact]
    public async Task ReportPage_DirectAndTargetedGet_ScopeToAdminWorkspace()
    {
        const long ownerBytes = 2L * 1024 * 1024;
        const long foreignBytes = 9L * 1024 * 1024;
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var (foreignClient, foreignId) = await ClientAsync(PlanTierLimits.Pro);
        var foreignWorkspace = await CreateWorkspaceAsync(foreignClient);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = ownerId,
                WorkspaceId = workspace.Id,
                ShareCode = Guid.CreateVersion7().ToString("N"),
                ContentType = "application/pdf",
                FileSize = ownerBytes,
            });
            db.Deliveries.Add(new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = foreignId,
                WorkspaceId = foreignWorkspace.Id,
                ShareCode = Guid.CreateVersion7().ToString("N"),
                ContentType = "application/pdf",
                FileSize = foreignBytes,
            });
            await db.SaveChangesAsync();
        }

        var direct = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceReport);
        Assert.Contains("id=\"workspace-report-body\"", direct);
        Assert.Contains("2.0 MB", direct);
        Assert.DoesNotContain("9.0 MB", direct);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get,
            RoutePaths.AccountWorkspaceReport);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#workspace-report-body");
        var targeted = await ownerClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains("2.0 MB", await targeted.Content.ReadAsStringAsync());

        var (viewerClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "report-page-viewer@example.com", WorkspaceRole.Viewer);
        await viewerClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        var viewer = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspaceReport);
        Assert.Contains("id=\"workspace-report-body\"", viewer);
        Assert.DoesNotContain("2.0 MB", viewer);
    }

    [Fact]
    public async Task WorkspacePage_DirectAndTargetedGet_PreserveRoleGates()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var direct = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspace);
        Assert.Contains("id=\"workspace-body\"", direct);
        Assert.Contains(WorkspaceName, direct);
        Assert.Contains("data-ai-action=\"workspace.invite\"", direct);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountWorkspace);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#workspace-body");
        var targeted = await ownerClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains(WorkspaceName, await targeted.Content.ReadAsStringAsync());

        var (viewerClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "workspace-page-viewer@example.com", WorkspaceRole.Viewer);
        await viewerClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        var viewer = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspace);
        Assert.Contains(WorkspaceName, viewer);
        Assert.DoesNotContain("data-ai-action=\"workspace.invite\"", viewer);

        var (freeClient, _) = await ClientAsync(PlanTierLimits.Free);
        var free = await freeClient.GetStringAsync(RoutePaths.AccountWorkspace);
        Assert.Contains("id=\"workspace-body\"", free);
        Assert.DoesNotContain("data-ai-action=\"workspace.create\"", free);
    }

    [Fact]
    public async Task WorkspaceSettings_DirectAndTargetedGet_IsolateBrandDomainAndSeats()
    {
        const string ownerHost = "owner-brand.example.test";
        const string foreignHost = "foreign-brand.example.test";
        const string ownerHeader = "Owner brand header";
        const string foreignHeader = "Foreign brand header";
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var (foreignClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var foreignWorkspace = await CreateWorkspaceAsync(foreignClient);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkspaceBrands.Add(new WorkspaceBrand { WorkspaceId = workspace.Id, HeaderText = ownerHeader });
            db.WorkspaceBrands.Add(new WorkspaceBrand { WorkspaceId = foreignWorkspace.Id, HeaderText = foreignHeader });
            db.WorkspaceDomains.Add(new WorkspaceDomain
            {
                WorkspaceId = workspace.Id,
                Host = ownerHost,
                VerificationToken = Guid.CreateVersion7().ToString("N"),
            });
            db.WorkspaceDomains.Add(new WorkspaceDomain
            {
                WorkspaceId = foreignWorkspace.Id,
                Host = foreignHost,
                VerificationToken = Guid.CreateVersion7().ToString("N"),
            });
            await db.SaveChangesAsync();
        }

        var brand = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceBrand);
        Assert.Contains(ownerHeader, brand);
        Assert.DoesNotContain(foreignHeader, brand);
        var domain = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceDomain);
        Assert.Contains(ownerHost, domain);
        Assert.DoesNotContain(foreignHost, domain);
        var seats = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceSeats);
        Assert.Contains("id=\"workspace-seats-body\"", seats);
        Assert.Contains("data-ai-action=\"seats.export\"", seats);

        foreach (var (route, selector) in new[]
        {
            (RoutePaths.AccountWorkspaceBrand, "workspace-brand-body"),
            (RoutePaths.AccountWorkspaceDomain, "workspace-domain-body"),
            (RoutePaths.AccountWorkspaceSeats, "workspace-seats-body"),
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add(UnpolyHeaders.Target, $"#{selector}");
            var response = await ownerClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"id=\"{selector}\"", await response.Content.ReadAsStringAsync());
        }

        var (viewerClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "workspace-settings-viewer@example.com", WorkspaceRole.Viewer);
        await viewerClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        var viewerBrand = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspaceBrand);
        var viewerDomain = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspaceDomain);
        var viewerSeats = await viewerClient.GetStringAsync(RoutePaths.AccountWorkspaceSeats);
        Assert.DoesNotContain(ownerHeader, viewerBrand);
        Assert.DoesNotContain(ownerHost, viewerDomain);
        Assert.DoesNotContain("data-ai-action=\"seats.export\"", viewerSeats);

        var foreignBrand = await foreignClient.GetStringAsync(RoutePaths.AccountWorkspaceBrand);
        var foreignDomain = await foreignClient.GetStringAsync(RoutePaths.AccountWorkspaceDomain);
        Assert.Contains(foreignHeader, foreignBrand);
        Assert.Contains(foreignHost, foreignDomain);
    }

    [Fact]
    public async Task WorkspaceIntegrations_DirectGet_ScopeConfigsAndNeverRenderSecrets()
    {
        const string ownerSink = "https://owner-siem.example.test/events";
        const string foreignSink = "https://foreign-siem.example.test/events";
        const string ownerBucket = "owner-backup-bucket";
        const string foreignBucket = "foreign-backup-bucket";
        const string channelCredential = "https://hooks.example.test/private-owner-secret";
        const string siemCredential = "private-siem-secret";
        const string backupCredential = "private-backup-secret";
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var (foreignClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var foreignWorkspace = await CreateWorkspaceAsync(foreignClient);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkspaceChannelHooks.Add(new WorkspaceChannelHook
            {
                WorkspaceId = workspace.Id,
                WebhookUrl = channelCredential,
            });
            db.SiemSinks.Add(new SiemSink
            {
                WorkspaceId = workspace.Id,
                Endpoint = ownerSink,
                Secret = siemCredential,
            });
            db.SiemSinks.Add(new SiemSink
            {
                WorkspaceId = foreignWorkspace.Id,
                Endpoint = foreignSink,
            });
            db.WorkspaceBackupPolicies.Add(new WorkspaceBackupPolicy
            {
                WorkspaceId = workspace.Id,
                BucketName = ownerBucket,
                AccessKeySecret = backupCredential,
            });
            db.WorkspaceBackupPolicies.Add(new WorkspaceBackupPolicy
            {
                WorkspaceId = foreignWorkspace.Id,
                BucketName = foreignBucket,
            });
            await db.SaveChangesAsync();
        }

        foreach (var (route, selector, control) in new[]
        {
            (RoutePaths.AccountWorkspaceChannel, "workspace-channel-body", "channel-kind"),
            (RoutePaths.AccountWorkspaceStreaming, "workspace-streaming-body", "siem-endpoint"),
            (RoutePaths.AccountWorkspaceBackup, "workspace-backup-body", "backup-endpoint"),
        })
        {
            var html = await ownerClient.GetStringAsync(route);
            Assert.Contains($"id=\"{selector}\"", html);
            Assert.Contains($"id=\"{control}\"", html);
            Assert.DoesNotContain(channelCredential, html);
            Assert.DoesNotContain(siemCredential, html);
            Assert.DoesNotContain(backupCredential, html);
            Assert.DoesNotContain(foreignSink, html);
            Assert.DoesNotContain(foreignBucket, html);

            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add(UnpolyHeaders.Target, $"#{selector}");
            var targeted = await ownerClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
            Assert.Contains($"id=\"{selector}\"", await targeted.Content.ReadAsStringAsync());
        }

        var ownerStreaming = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceStreaming);
        var ownerBackup = await ownerClient.GetStringAsync(RoutePaths.AccountWorkspaceBackup);
        Assert.Contains(ownerSink, ownerStreaming);
        Assert.Contains(ownerBucket, ownerBackup);

        var (viewerClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "integration-viewer@example.com", WorkspaceRole.Viewer);
        await viewerClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        foreach (var (route, control) in new[]
        {
            (RoutePaths.AccountWorkspaceChannel, "channel-kind"),
            (RoutePaths.AccountWorkspaceStreaming, "siem-endpoint"),
            (RoutePaths.AccountWorkspaceBackup, "backup-endpoint"),
        })
        {
            var html = await viewerClient.GetStringAsync(route);
            Assert.DoesNotContain($"id=\"{control}\"", html);
            Assert.DoesNotContain(ownerSink, html);
            Assert.DoesNotContain(ownerBucket, html);
        }
    }

    [Fact]
    public async Task Audit_ExportsCsvWithAHeaderRow()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);

        var response = await client.GetAsync("/api/workspace/audit/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("timestamp_utc,actor_name,actor_email,action,metadata", csv, StringComparison.Ordinal);
        Assert.Contains("workspacecreated", csv, StringComparison.Ordinal);
    }

    // ── Admin report (#300) ──

    [Fact]
    public async Task Report_CountsWorkspaceDeliveriesAndLeavesPersonalSharesOut()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        await SeedDeliveryAsync(ownerId, workspace.Id, bytes: 2048, downloads: 3);
        // A personal share by the same account: an admin report is not a licence to read it.
        await SeedDeliveryAsync(ownerId, workspaceId: null, bytes: 999_999, downloads: 9);

        var report = await ownerClient.GetFromJsonAsync<WorkspaceReportPayload>("/api/workspace/report");

        Assert.NotNull(report);
        Assert.Equal(1, report.Deliveries);
        Assert.Equal(2048, report.BytesUploaded);
        Assert.Equal(3, report.Downloads);
        Assert.Equal(1, report.SeatsUsed);

        var owner = report.Members.Single(m => m.UserId == ownerId);
        Assert.Equal(1, owner.Deliveries);
        Assert.Equal(2048, owner.BytesUploaded);
    }

    [Fact]
    public async Task Report_IsRefusedToNonAdmins()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        var token = await SeedInviteAsync(workspace.Id, "report@example.com");
        await memberClient.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });

        var response = await memberClient.GetAsync("/api/workspace/report");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> ClientAsync(string plan)
    {
        // One account per case: a seat is account-wide state, so a shared user would carry
        // one case's workspace into the next.
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, plan);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<WorkspaceDto> CreateWorkspaceAsync(HttpClient client, int seats = Seats)
    {
        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = WorkspaceName, Seats = seats });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    /// <summary>
    /// Seeds an invite with a token the test knows. The API only ever stores the hash and
    /// mails the raw value, so there is no honest way to read one back out of a response.
    /// </summary>
    private async Task<string> SeedInviteAsync(
        Guid workspaceId,
        string email,
        WorkspaceRole role = WorkspaceRole.Member,
        DateTime? expiresAt = null)
    {
        var token = Guid.NewGuid().ToString("N");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            InviteEmail = email,
            Role = role,
            InviteTokenHash = WorkspaceService.HashToken(token),
            InviteExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(WorkspaceMember.InviteValidDays),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return token;
    }

    /// <summary>Replaces the open transfer's hash with one the test can present.</summary>
    private async Task<string> SeedTransferTokenAsync(Guid workspaceId)
    {
        var token = Guid.NewGuid().ToString("N");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transfer = await db.WorkspaceTransfers
            .SingleAsync(t => t.WorkspaceId == workspaceId && t.AcceptedAt == null && t.CancelledAt == null);
        transfer.TokenHash = WorkspaceService.HashToken(token);
        await db.SaveChangesAsync();

        return token;
    }

    private async Task<Guid> SeatIdAsync(Guid workspaceId, Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.RevokedAt == null)
            .Select(m => m.Id)
            .SingleAsync();
    }

    private async Task AssertOwnerAsync(Guid workspaceId, Guid expectedOwnerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == workspaceId);
        Assert.Equal(expectedOwnerId, workspace.OwnerId);
    }

    private async Task SeedDeliveryAsync(Guid senderId, Guid? workspaceId, long bytes, int downloads)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = senderId,
            WorkspaceId = workspaceId,
            FileName = "report.bin",
            ContentType = "application/octet-stream",
            FileSize = bytes,
            TotalBytes = bytes,
            ShareCode = Guid.NewGuid().ToString("N")[..12],
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            DownloadCount = downloads,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
    }

    // Local response shapes: the endpoints return records whose members the test only
    // needs to read, and binding to the production DTOs would drag their namespaces in.
    private sealed record WorkspaceAuditRowDto(string Action, string ActorEmail, DateTime CreatedAt);
    private sealed record WorkspaceAuditPageDto(List<WorkspaceAuditRowDto> Rows, int Total);
    private sealed record WorkspaceReportMemberDto(Guid UserId, int Deliveries, long BytesUploaded);
    private sealed record WorkspaceReportPayload(
        int Deliveries,
        long BytesUploaded,
        int Downloads,
        int SeatsUsed,
        List<WorkspaceReportMemberDto> Members);
}
