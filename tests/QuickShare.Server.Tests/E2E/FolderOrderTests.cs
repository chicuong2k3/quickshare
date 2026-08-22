using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Manual folder order (#275).
///
/// A reorder is one intention, so it is one request carrying the whole arrangement rather than a
/// sequence of moves — every intermediate state of a series of moves is a state somebody could
/// see. Positions are assigned from the sequence rather than trusted from the caller, so a
/// duplicate or a gap cannot produce a list that sorts differently for the next reader.
/// </summary>
public sealed class FolderOrderTests : IClassFixture<TestWebAppFactory>
{
    private const string OrderUrl = "/api/workspace/folders/order";

    private readonly TestWebAppFactory _factory;

    public FolderOrderTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WithoutAnArrangement_FoldersAreAlphabetical()
    {
        var (client, _) = await WorkspaceAsync();
        await FolderAsync(client, "Zebra");
        await FolderAsync(client, "Alpha");

        var names = await NamesAsync(client);

        // Everything that predates ordering sits at position zero, and alphabetical is a better
        // answer there than insertion order.
        Assert.Equal(["Alpha", "Zebra"], names);
    }

    [Fact]
    public async Task TheArrangementIsWhatTheListShows()
    {
        var (client, _) = await WorkspaceAsync();
        var zebra = await FolderAsync(client, "Zebra");
        var alpha = await FolderAsync(client, "Alpha");

        var response = await client.PatchAsJsonAsync(OrderUrl,
            new ReorderFoldersRequest { Order = [zebra, alpha] });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(["Zebra", "Alpha"], await NamesAsync(client));
    }

    [Fact]
    public async Task PositionsComeFromTheSequence_NotFromTheCaller()
    {
        var (client, workspaceId) = await WorkspaceAsync();
        var first = await FolderAsync(client, "Alpha");
        var second = await FolderAsync(client, "Beta");

        // The same id twice: a caller cannot talk two folders into the same position.
        await client.PatchAsJsonAsync(OrderUrl, new ReorderFoldersRequest { Order = [second, second, first] });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = await db.WorkspaceFolders.AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId)
            .Select(f => f.SortOrder)
            .ToListAsync();

        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public async Task AnotherWorkspacesFolderIsNotReordered()
    {
        var (client, _) = await WorkspaceAsync();
        await FolderAsync(client, "Mine");

        var (stranger, strangerWorkspace) = await WorkspaceAsync();
        var theirs = await FolderAsync(stranger, "Theirs");

        var response = await client.PatchAsJsonAsync(OrderUrl, new ReorderFoldersRequest { Order = [theirs] });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folder = await db.WorkspaceFolders.AsNoTracking().SingleAsync(f => f.Id == theirs);

        // An id from somebody else's tree is silently not found — the same answer as it never
        // existing.
        Assert.Equal(strangerWorkspace, folder.WorkspaceId);
        Assert.Equal(0, folder.SortOrder);
    }

    [Fact]
    public async Task AnEmptyArrangementIsRefused()
    {
        var (client, _) = await WorkspaceAsync();

        var response = await client.PatchAsJsonAsync(OrderUrl, new ReorderFoldersRequest { Order = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FoldersPage_OffersBothWaysToMoveARow()
    {
        var (client, _) = await WorkspaceAsync();
        await FolderAsync(client, "Alpha");

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountWorkspaceFolders);
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // A reorder that only works by dragging is a reorder keyboard users cannot do at all.
        Assert.Contains("draggable=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("folder.moveUp", html, StringComparison.Ordinal);
        Assert.Contains("folder.moveDown", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var created = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Order {Guid.NewGuid():N}"[..16], Seats = 3 });
        created.EnsureSuccessStatusCode();

        return (client, (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id);
    }

    private static async Task<Guid> FolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/folders", new CreateFolderRequest { Name = name });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FolderDto>())!.Id;
    }

    private static async Task<List<string>> NamesAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<FolderListDto>("/api/workspace/folders");

        return response!.Folders.Select(f => f.Name).ToList();
    }
}
