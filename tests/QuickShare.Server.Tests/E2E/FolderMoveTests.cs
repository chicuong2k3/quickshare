using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Bulk;
using QuickShare.Features.Deliveries.Clone;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Filing deliveries after upload (issues #244 move/copy, #284 move many at once).
///
/// The interesting rules are all about what a move must *not* quietly do: it must not turn a
/// personal share into a team one, must not reach into another workspace's tree, and must not
/// let a file escape a folder policy by arriving from somewhere else.
/// </summary>
public sealed class FolderMoveTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public FolderMoveTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Move_FilesSelectedDeliveriesIntoTheFolder()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Campaign");
        var first = await SeedAsync(userId, workspaceId);
        var second = await SeedAsync(userId, workspaceId);

        var response = await MoveAsync(client, [first, second], folderId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<BulkDeliveriesResponse>())!;
        Assert.Equal(2, result.Affected);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Deliveries.CountAsync(d => d.FolderId == folderId));
    }

    [Fact]
    public async Task Move_WithNoFolderTakesThemBackOut()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Campaign");
        var deliveryId = await SeedAsync(userId, workspaceId, folderId);

        (await MoveAsync(client, [deliveryId], null)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // "Out of any folder" is a destination, not a missing one.
        Assert.Null(await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId).Select(d => d.FolderId).SingleAsync());
    }

    [Fact]
    public async Task Move_LeavesAPersonalDeliveryWhereItIs()
    {
        var (client, _, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Campaign");
        var personal = await SeedAsync(userId, workspaceId: null);

        var result = (await (await MoveAsync(client, [personal], folderId))
            .Content.ReadFromJsonAsync<BulkDeliveriesResponse>())!;

        // Filing a personal share into a shared folder is a *share*, not a move — it would hand
        // colleagues a file its owner never offered them.
        Assert.Equal(0, result.Affected);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Move_RefusesAFolderFromAnotherWorkspace()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var deliveryId = await SeedAsync(userId, workspaceId);

        var (stranger, _, _) = await WorkspaceAsync();
        var theirFolder = await FolderAsync(stranger, "Theirs");

        var response = await MoveAsync(client, [deliveryId], theirFolder);

        // Refused rather than reported as skipped: the caller named a destination that is not
        // theirs, and silence would look like the move worked.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Move_IntoALockedFolderTightensTheDeliveryToItsPolicy()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Locked");
        await LockFolderAsync(folderId, expiryHours: 1, privacy: true);

        var deliveryId = await SeedAsync(userId, workspaceId);

        (await MoveAsync(client, [deliveryId], folderId)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        // Locked defaults only ever tighten (issue #245) — arriving from somewhere else is not
        // a way around them.
        Assert.True(delivery.PrivacyMode);
        Assert.True(delivery.ExpiresAt < DateTime.UtcNow.AddHours(2));
    }

    [Fact]
    public async Task Move_IsRefusedByAFolderThatDemandsAPassword()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Contracts");
        await LockFolderAsync(folderId, passwordRequired: true);

        var deliveryId = await SeedAsync(userId, workspaceId);

        var response = await MoveAsync(client, [deliveryId], folderId);

        // Same refusal a mount write gets: this path has nowhere to ask for the password the
        // folder requires.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Copy_LandsTheDuplicateInTheChosenFolder()
    {
        var (client, workspaceId, userId) = await WorkspaceAsync();
        var folderId = await FolderAsync(client, "Archive");
        var deliveryId = await SeedAsync(userId, workspaceId);

        var response = await client.PostAsync($"/api/deliveries/{deliveryId}/clone?folderId={folderId}", null);
        response.EnsureSuccessStatusCode();

        var clone = (await response.Content.ReadFromJsonAsync<CloneDeliveryResponse>())!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var copied = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == clone.Id);

        Assert.Equal(folderId, copied.FolderId);
        // A copy is a new link; the original stays exactly where it was.
        Assert.Null(await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId).Select(d => d.FolderId).SingleAsync());
    }

    [Fact]
    public async Task Copy_RefusesAFolderTheDeliveryCannotReach()
    {
        var (client, _, userId) = await WorkspaceAsync();
        var personal = await SeedAsync(userId, workspaceId: null);
        var folderId = await FolderAsync(client, "Archive");

        var response = await client.PostAsync($"/api/deliveries/{personal}/clone?folderId={folderId}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId, Guid UserId)> WorkspaceAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var created = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Files {Guid.NewGuid():N}"[..16], Seats = 3 });
        created.EnsureSuccessStatusCode();

        return (client, (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id, user.Id);
    }

    private static async Task<Guid> FolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = $"{name}-{Guid.NewGuid():N}"[..12] });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FolderDto>())!.Id;
    }

    private async Task LockFolderAsync(
        Guid folderId, double? expiryHours = null, bool privacy = false, bool passwordRequired = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folder = await db.WorkspaceFolders.SingleAsync(f => f.Id == folderId);
        folder.DefaultsLocked = true;
        folder.DefaultExpiryHours = expiryHours;
        folder.DefaultPrivacyMode = privacy;
        folder.DefaultPasswordRequired = passwordRequired;

        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid[] ids, Guid? folderId) =>
        client.PostAsJsonAsync("/api/deliveries/bulk", new BulkDeliveriesRequest
        {
            Ids = ids,
            Action = BulkDeliveriesHandler.ActionMove,
            FolderId = folderId,
        });

    private async Task<Guid> SeedAsync(Guid userId, Guid? workspaceId, Guid? folderId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            WorkspaceId = workspaceId,
            FolderId = folderId,
            FileName = "brief.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ShareCode = Guid.NewGuid().ToString("N")[..12],
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            MaxDownloads = 10,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        return delivery.Id;
    }
}
