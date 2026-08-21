using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments.Billing;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Tenant isolation (issue #190). Two workspaces, two owners, and one question asked of every
/// surface that takes an id: can org B reach org A's things by holding the right GUID?
///
/// This exists because the isolation mechanism is an explicit <c>Where</c> per query rather
/// than an EF global filter — a deliberate choice, since a global filter is easy to add and
/// easy to lose to <c>IgnoreQueryFilters</c>, raw SQL or a projection built before it applies,
/// and its failure mode is silent cross-tenant reads. An explicit predicate is only as good as
/// the test that proves every query carries one, so this file is that proof rather than a
/// nice-to-have.
///
/// Every case probes with a **real, valid id from the other tenant**. Asserting on a random
/// GUID would pass even if scoping were removed entirely.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public TenantIsolationTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Folder_FromAnotherWorkspaceIsInvisibleToEverySurface()
    {
        var (alice, _) = await TenantAsync("Alice Co");
        var folder = await CreateFolderAsync(alice.Client, "Alice private");

        var (bob, _) = await TenantAsync("Bob Co");

        // Not in the list...
        var list = await bob.Client.GetFromJsonAsync<FolderListDto>("/api/workspace/folders");
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Folders, f => f.Id == folder.Id);

        // ...and not reachable by id, on any of the three routes that take one.
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.GetAsync($"/api/workspace/folders/{folder.Id}/prefill")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.PatchAsJsonAsync($"/api/workspace/folders/{folder.Id}",
                new RenameFolderRequest { Name = "Bob was here" })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.DeleteAsync($"/api/workspace/folders/{folder.Id}")).StatusCode);

        // The folder is untouched by any of that.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WorkspaceFolders.AsNoTracking().SingleAsync(f => f.Id == folder.Id);
        Assert.Equal("Alice private", stored.Name);
    }

    [Fact]
    public async Task Folder_ProbeFromAnotherWorkspaceIsRecordedButNotRevealed()
    {
        var (alice, _) = await TenantAsync("Alice Probe Co");
        var folder = await CreateFolderAsync(alice.Client, "Probe target");

        var (bob, bobWorkspaceId) = await TenantAsync("Bob Probe Co");

        var real = await bob.Client.GetAsync($"/api/workspace/folders/{folder.Id}/prefill");
        var imaginary = await bob.Client.GetAsync($"/api/workspace/folders/{Guid.NewGuid()}/prefill");

        // Identical answers: the response must not tell Bob that Alice's folder exists.
        Assert.Equal(HttpStatusCode.NotFound, real.StatusCode);
        Assert.Equal(imaginary.StatusCode, real.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var blocked = await db.SecurityEvents
            .AsNoTracking()
            .Where(e => e.UserId == bob.UserId && e.EventType == SecurityEventType.CrossTenantAccessBlocked)
            .ToListAsync();

        // Server-side, though, exactly one reach is on the record — and the miss on a GUID that
        // belongs to nobody is not, or the signal would be pure noise.
        var recorded = Assert.Single(blocked);
        Assert.Equal(bobWorkspaceId, recorded.WorkspaceId);
        Assert.Contains(folder.Id.ToString(), recorded.MetadataJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Workspace_ManagementSurfacesAreScopedToTheCallerOwnTenant()
    {
        var (alice, aliceWorkspaceId) = await TenantAsync("Alice Mgmt Co");
        var aliceSeatId = await OwnerSeatIdAsync(aliceWorkspaceId);

        var (bob, bobWorkspaceId) = await TenantAsync("Bob Mgmt Co");

        // GET returns Bob's own workspace, never the one whose id he might hold.
        var seen = await bob.Client.GetFromJsonAsync<WorkspaceDto>("/api/workspace");
        Assert.NotNull(seen);
        Assert.Equal(bobWorkspaceId, seen.Id);
        Assert.NotEqual(aliceWorkspaceId, seen.Id);

        // Alice's seat id is a valid GUID that Bob simply has no claim on.
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.DeleteAsync($"/api/workspace/members/{aliceSeatId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.PatchAsJsonAsync($"/api/workspace/members/{aliceSeatId}",
                new ChangeMemberRoleRequest { Role = "viewer" })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.PostAsJsonAsync("/api/workspace/transfer",
                new QuickShare.Features.Workspaces.Transfer.InitiateTransferRequest { MemberId = aliceSeatId })).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seat = await db.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.Id == aliceSeatId);
        Assert.Equal(WorkspaceRole.Owner, seat.Role);
        Assert.Null(seat.RevokedAt);
    }

    [Fact]
    public async Task Audit_AndReport_ShowOnlyTheCallerOwnWorkspace()
    {
        var (alice, aliceWorkspaceId) = await TenantAsync("Alice Audit Co");
        await CreateFolderAsync(alice.Client, "Alice audit folder");

        var (bob, bobWorkspaceId) = await TenantAsync("Bob Audit Co");

        var audit = await bob.Client.GetFromJsonAsync<AuditPageProbe>("/api/workspace/audit");
        Assert.NotNull(audit);
        // Bob sees his own workspace-created event and nothing of Alice's.
        Assert.DoesNotContain(audit.Rows, r => r.ActorId == alice.UserId);

        var report = await bob.Client.GetFromJsonAsync<ReportProbe>("/api/workspace/report");
        Assert.NotNull(report);
        Assert.Equal(bobWorkspaceId, report.WorkspaceId);
        Assert.NotEqual(aliceWorkspaceId, report.WorkspaceId);
    }

    [Fact]
    public async Task Delivery_FromAnotherTenantIsNotListedAndCannotBeMutated()
    {
        var (alice, aliceWorkspaceId) = await TenantAsync("Alice Files Co");
        var deliveryId = await SeedDeliveryAsync(alice.UserId, aliceWorkspaceId);

        var (bob, _) = await TenantAsync("Bob Files Co");

        var list = await bob.Client.GetFromJsonAsync<DeliveryPageProbe>("/api/deliveries");
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Items, d => d.Id == deliveryId);

        // Mutation paths match on SenderId, so a valid id from another tenant is still a miss.
        var trashed = await bob.Client.DeleteAsync($"/api/deliveries/{deliveryId}");
        Assert.NotEqual(HttpStatusCode.OK, trashed.StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent, trashed.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
        Assert.Null(delivery.DeletedAt);
    }

    [Fact]
    public async Task Invoice_FromAnotherAccountIsNotReadable()
    {
        var (alice, _) = await TenantAsync("Alice Money Co");

        Guid invoiceId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new InvoiceService(db);
            await service.AddOrUpdateLineAsync(
                alice.UserId, InvoiceLineKind.Plan, "Pro (monthly)", 1m, 50_000m, CancellationToken.None);
            var invoice = (await service.IssueAsync(alice.UserId, null, alreadyPaid: true, CancellationToken.None))!;
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        var (bob, _) = await TenantAsync("Bob Money Co");

        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.Client.GetAsync($"/api/billing/invoices/{invoiceId}")).StatusCode);

        var bobList = await bob.Client.GetFromJsonAsync<List<InvoiceDto>>("/api/billing/invoices");
        Assert.NotNull(bobList);
        Assert.DoesNotContain(bobList, i => i.Id == invoiceId);
    }

    [Fact]
    public async Task Streaming_ConfigIsPerWorkspaceAndNeverLeaksTheSecret()
    {
        var (alice, _) = await TenantAsync("Alice SIEM Co");

        var save = await alice.Client.PutAsJsonAsync("/api/workspace/streaming",
            new QuickShare.Features.Workspaces.Streaming.SiemSinkRequest
            {
                Endpoint = "https://siem.alice.example/ingest",
                Format = SiemFormat.Json,
                Secret = "alice-super-secret",
            });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        // The secret is never echoed back, not even to the admin who set it.
        var body = await save.Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice-super-secret", body, StringComparison.Ordinal);

        var (bob, _) = await TenantAsync("Bob SIEM Co");
        var bobSink = await bob.Client.GetAsync("/api/workspace/streaming");
        Assert.Equal(HttpStatusCode.OK, bobSink.StatusCode);
        var bobBody = await bobSink.Content.ReadAsStringAsync();
        Assert.DoesNotContain("siem.alice.example", bobBody, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private sealed record Tenant(HttpClient Client, Guid UserId);

    /// <summary>A paid account with its own workspace — the smallest complete tenant.</summary>
    private async Task<(Tenant Tenant, Guid WorkspaceId)> TenantAsync(string name)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = name, Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (new Tenant(client, user.Id), workspace.Id);
    }

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private async Task<Guid> OwnerSeatIdAsync(Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner)
            .Select(m => m.Id)
            .SingleAsync();
    }

    private async Task<Guid> SeedDeliveryAsync(Guid senderId, Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.CreateVersion7();
        db.Deliveries.Add(new Delivery
        {
            Id = id,
            SenderId = senderId,
            WorkspaceId = workspaceId,
            FileName = "confidential.bin",
            ContentType = "application/octet-stream",
            FileSize = 2048,
            TotalBytes = 2048,
            ShareCode = Guid.NewGuid().ToString("N")[..12],
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
        return id;
    }

    // Local response shapes: only the fields these assertions read.
    private sealed record AuditRowProbe(Guid ActorId, string Action);
    private sealed record AuditPageProbe(List<AuditRowProbe> Rows, int Total);
    private sealed record ReportProbe(Guid WorkspaceId, int Deliveries);
    private sealed record DeliveryProbe(Guid Id, string FileName);
    private sealed record DeliveryPageProbe(List<DeliveryProbe> Items);
}
