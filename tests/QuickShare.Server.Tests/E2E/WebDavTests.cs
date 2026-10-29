using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Developer;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The WebDAV mount (issue #195).
///
/// What is worth asserting is not that the XML parses — it is that a drive is the *workspace*
/// and nothing else: no anonymous mount, no personal deliveries, no way past the scope on the
/// key, and the same refusals (checkout lock, quota, folder policy) the upload form gets. A
/// mount that quietly relaxed one of those would be a second, weaker door into the same files.
/// </summary>
public sealed class WebDavTests : IClassFixture<TestWebAppFactory>
{
    private const string Dav = "/dav";
    private const string Propfind = "PROPFIND";
    private const string Mkcol = "MKCOL";
    private static readonly XNamespace DavNs = "DAV:";

    private readonly TestWebAppFactory _factory;

    public WebDavTests(TestWebAppFactory factory) => _factory = factory;

    // ── Getting in ──

    [Fact]
    public async Task Mount_IsNeverAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await SendAsync(client, Propfind, Dav, key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Without the challenge, a client has nothing to prompt its user with.
        Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_AdvertisesClassOneWithoutPretendingToLock()
    {
        var client = _factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Options.Method, Dav, key: null);

        // Answered before authentication: every client probes before it has anything to send.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("DAV").Single());
        Assert.DoesNotContain("LOCK", response.Content.Headers.Allow.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mount_RefusesAKeyWithNoWorkspaceSeat()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var owner = _factory.CreateAuthenticatedClient(providerKey);
        var key = await ApiKeyAsync(owner, ApiScopes.Read);

        // A personal account has nothing to mount, and an empty drive would be a worse answer.
        var response = await SendAsync(_factory.CreateClient(), Propfind, Dav, key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Listing ──

    [Fact]
    public async Task Propfind_ListsWorkspaceFoldersAndFiles()
    {
        var (client, key, workspaceId) = await MountAsync();
        await FolderAsync(client, "Campaign");
        await SeedDeliveryAsync(workspaceId, "brief.pdf");

        var response = await SendAsync(_factory.CreateClient(), Propfind, Dav, key, depth: "1");
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);

        var names = await DisplayNamesAsync(response);
        Assert.Contains("Campaign", names);
        Assert.Contains("brief.pdf", names);
    }

    [Fact]
    public async Task Propfind_DoesNotShowPersonalDeliveries()
    {
        var (_, key, _) = await MountAsync();
        await SeedDeliveryAsync(workspaceId: null, "private-notes.pdf");

        var response = await SendAsync(_factory.CreateClient(), Propfind, Dav, key, depth: "1");

        // A mount is a team surface; somebody's personal pile is not part of it.
        Assert.DoesNotContain("private-notes.pdf", await DisplayNamesAsync(response));
    }

    [Fact]
    public async Task Propfind_RefusesInfiniteDepth()
    {
        var (_, key, _) = await MountAsync();

        var response = await SendAsync(_factory.CreateClient(), Propfind, Dav, key, depth: "infinity");

        // One accidental client setting must not walk the whole workspace.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("propfind-finite-depth", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Propfind_SeesInsideAFolder()
    {
        var (client, key, workspaceId) = await MountAsync();
        var folderId = await FolderAsync(client, "Campaign");
        await SeedDeliveryAsync(workspaceId, "inside.pdf", folderId);

        var response = await SendAsync(_factory.CreateClient(), Propfind, $"{Dav}/Campaign", key, depth: "1");

        Assert.Contains("inside.pdf", await DisplayNamesAsync(response));
    }

    // ── Writing ──

    [Fact]
    public async Task Put_CreatesADeliveryInTheFolderItLandedIn()
    {
        var (client, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);
        await FolderAsync(client, "Reports");

        var response = await PutAsync(key, $"{Dav}/Reports/monday.csv", "date,total\n");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking()
            .SingleAsync(d => d.WorkspaceId == workspaceId && d.FileName == "monday.csv");

        Assert.NotNull(delivery.FolderId);
        Assert.Equal(DeliverySources.Mount, delivery.Source);
        // It is a real delivery: it has a link, an expiry and a place in the workspace.
        Assert.NotEqual(string.Empty, delivery.ShareCode);
        Assert.NotNull(delivery.ExpiresAt);
    }

    [Fact]
    public async Task Put_OverAnExistingNameReplacesTheFileAndKeepsTheLink()
    {
        var (_, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);

        (await PutAsync(key, $"{Dav}/report.csv", "v1")).EnsureSuccessStatusCode();
        var first = await ShareCodeAsync(workspaceId, "report.csv");

        (await PutAsync(key, $"{Dav}/report.csv", "v2 is longer")).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId && d.FileName == "report.csv" && d.DeletedAt == null)
            .ToListAsync();

        // One name, one file — a sync client cannot cope with two — and the link survives,
        // which is exactly what "replace file, keep link" (#72) already means here.
        var single = Assert.Single(deliveries);
        Assert.Equal(first, single.ShareCode);
    }

    [Fact]
    public async Task Put_IsRefusedByAReadOnlyKey()
    {
        var (_, key, _) = await MountAsync(ApiScopes.Read);

        var response = await PutAsync(key, $"{Dav}/nope.txt", "x");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_IsRefusedWhileSomebodyElseHoldsTheCheckout()
    {
        var (_, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);
        (await PutAsync(key, $"{Dav}/locked.txt", "v1")).EnsureSuccessStatusCode();
        await LockAsync(workspaceId, "locked.txt");

        var response = await PutAsync(key, $"{Dav}/locked.txt", "v2");

        // The drive and the web UI refuse the same file for the same reason (#308).
        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
    }

    [Fact]
    public async Task Mkcol_CreatesAWorkspaceFolder()
    {
        var (_, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var response = await SendAsync(_factory.CreateClient(), Mkcol, $"{Dav}/From%20Finder", key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.WorkspaceFolders.AnyAsync(f => f.WorkspaceId == workspaceId && f.Name == "From Finder"));
    }

    [Fact]
    public async Task Mkcol_StillStopsAtTwoLevels()
    {
        var (client, key, _) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);
        var parentId = await FolderAsync(client, "Client");
        await FolderAsync(client, "Project", parentId);

        var response = await SendAsync(_factory.CreateClient(), Mkcol, $"{Dav}/Client/Project/Deeper", key);

        // A mount is not a way around the folder depth cap.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Reading and removing ──

    [Fact]
    public async Task Get_ReturnsTheBytesWithoutCountingADownload()
    {
        var (_, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);
        (await PutAsync(key, $"{Dav}/notes.txt", "hello mount")).EnsureSuccessStatusCode();

        var response = await SendAsync(_factory.CreateClient(), HttpMethod.Get.Method, $"{Dav}/notes.txt", key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello mount", await response.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking()
            .SingleAsync(d => d.WorkspaceId == workspaceId && d.FileName == "notes.txt");

        // The team reading its own file is not a recipient download, and counting it would
        // corrupt the only number the sender asked for.
        Assert.Equal(0, delivery.DownloadCount);
    }

    [Fact]
    public async Task Delete_NeedsTheManageScopeAndTrashesRatherThanDestroys()
    {
        var (_, key, workspaceId) = await MountAsync(ApiScopes.Read + "," + ApiScopes.Write);
        (await PutAsync(key, $"{Dav}/temp.txt", "x")).EnsureSuccessStatusCode();

        // A key that drops a nightly report should not be able to empty the drive.
        var refused = await SendAsync(_factory.CreateClient(), HttpMethod.Delete.Method, $"{Dav}/temp.txt", key);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var (_, manageKey, _) = await MountAsync(
            ApiScopes.Read + "," + ApiScopes.Write + "," + ApiScopes.Manage, workspaceId);

        var deleted = await SendAsync(_factory.CreateClient(), HttpMethod.Delete.Method, $"{Dav}/temp.txt", manageKey);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking()
            .SingleAsync(d => d.WorkspaceId == workspaceId && d.FileName == "temp.txt");

        // Soft: a mistaken rm on a mounted drive is what the trash window exists for (#99).
        Assert.NotNull(delivery.DeletedAt);
    }

    [Fact]
    public async Task Delete_RefusesACollection()
    {
        var (client, key, _) = await MountAsync(
            ApiScopes.Read + "," + ApiScopes.Write + "," + ApiScopes.Manage);
        await FolderAsync(client, "Keep");

        var response = await SendAsync(_factory.CreateClient(), HttpMethod.Delete.Method, $"{Dav}/Keep", key);

        // Deleting a folder would cascade over deliveries the client never listed.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ── The setup screen ──

    [Fact]
    public async Task DeveloperPage_ShowsTheMountAddressOnceThereIsASeat()
    {
        var (client, _, _) = await MountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/developer");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // The address and the one command a team has to run — an endpoint nobody can find is
        // the defect the batch-11 audit spent its time on.
        Assert.Contains(Dav, html, StringComparison.Ordinal);
        Assert.Contains("rclone config create", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeveloperPage_OffersNoMountWithoutAWorkspace()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/developer");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // An address that would answer 401 forever is worse than saying why.
        Assert.DoesNotContain("rclone config create", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, string Key, Guid WorkspaceId)> MountAsync(
        string scopes = ApiScopes.Read, Guid? existingWorkspace = null)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var workspaceId = existingWorkspace;
        if (workspaceId is null)
        {
            var created = await client.PostAsJsonAsync("/api/workspace",
                new CreateWorkspaceRequest { Name = $"Dav {Guid.NewGuid():N}"[..16], Seats = 3 });
            created.EnsureSuccessStatusCode();
            workspaceId = (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id;
        }
        else
        {
            await JoinAsync(user, workspaceId.Value);
        }

        return (client, await ApiKeyAsync(client, scopes), workspaceId.Value);
    }

    private static async Task<string> ApiKeyAsync(HttpClient client, string scopes)
    {
        var response = await client.PostAsJsonAsync("/api/developer/keys",
            new CreateApiKeyRequest { Name = "mount", Scopes = scopes });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApiKeyDto>())!.Key!;
    }

    /// <summary>Seats a second account in an existing workspace, straight through the store.</summary>
    private async Task JoinAsync(User user, Guid workspaceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = WorkspaceRole.Admin,
            InviteEmail = user.Email,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> FolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = name, ParentId = parentId });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FolderDto>())!.Id;
    }

    private async Task SeedDeliveryAsync(Guid? workspaceId, string fileName, Guid? folderId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            FolderId = folderId,
            FileName = fileName,
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ShareCode = Guid.NewGuid().ToString("N")[..12],
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            MaxDownloads = 10,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });

        await db.SaveChangesAsync();
    }

    private async Task<string> ShareCodeAsync(Guid workspaceId, string fileName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Deliveries.AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId && d.FileName == fileName && d.DeletedAt == null)
            .Select(d => d.ShareCode)
            .SingleAsync();
    }

    /// <summary>Checks the file out to somebody who is not the caller (issue #308).</summary>
    private async Task LockAsync(Guid workspaceId, string fileName)
    {
        var holder = await _factory.SeedUserAsync(Guid.NewGuid().ToString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.SingleAsync(d => d.WorkspaceId == workspaceId && d.FileName == fileName);

        db.FileLocks.Add(new FileLock
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            LockedById = holder.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string url, string? key, string? depth = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (key is not null)
            request.Headers.Authorization = Basic(key);

        if (depth is not null)
            request.Headers.Add("Depth", depth);

        return client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutAsync(string key, string url, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = Basic(key);

        return _factory.CreateClient().SendAsync(request);
    }

    /// <summary>Username ignored, password is the key — the shape every client speaks.</summary>
    private static AuthenticationHeaderValue Basic(string key) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"quickshare:{key}")));

    private static async Task<List<string>> DisplayNamesAsync(HttpResponseMessage response)
    {
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());

        return xml.Descendants(DavNs + "displayname").Select(e => e.Value).ToList();
    }
}
