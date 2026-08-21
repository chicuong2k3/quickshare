using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Features.Workspaces.Templates;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Shared workspace folders (#151), per-folder upload defaults (#245) and folder templates
/// (#234).
///
/// The invariants worth defending: deleting a folder never deletes files; nesting stops at two
/// levels; unlocked defaults are a prefill the sender can override while locked ones are policy
/// the commit path enforces; a lock set on a parent cannot be escaped by uploading one level
/// deeper; and applying a template twice is safe because it reuses by name instead of failing.
/// </summary>
public sealed class WorkspaceFolderTests : IClassFixture<TestWebAppFactory>
{
    private const string WorkspaceName = "Agency";
    private const string RootFolder = "Campaign Assets";

    private readonly TestWebAppFactory _factory;

    public WorkspaceFolderTests(TestWebAppFactory factory) => _factory = factory;

    // ── Structure (#151) ──

    [Fact]
    public async Task Create_RequiresAWorkspace()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);

        var response = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = RootFolder });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_NestsOneLevelAndRefusesGrandchildren()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);

        var root = await CreateFolderAsync(client, RootFolder);
        var child = await CreateFolderAsync(client, "Q4", root.Id);
        Assert.Equal(root.Id, child.ParentId);

        var grandchild = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = "Week 1", ParentId = child.Id });

        Assert.Equal(HttpStatusCode.BadRequest, grandchild.StatusCode);
    }

    [Fact]
    public async Task Create_RefusesADuplicateSiblingName()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        await CreateFolderAsync(client, RootFolder);

        var duplicate = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = RootFolder });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Delete_IsRefusedWhileTheFolderStillHoldsAnything()
    {
        var (client, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(client);
        var root = await CreateFolderAsync(client, RootFolder);
        var child = await CreateFolderAsync(client, "Q4", root.Id);

        // A folder with a child folder is not empty.
        var withChild = await client.DeleteAsync($"/api/workspace/folders/{root.Id}");
        Assert.Equal(HttpStatusCode.Conflict, withChild.StatusCode);

        await SeedDeliveryAsync(ownerId, workspace.Id, child.Id);

        // Nor is one with a delivery filed under it — deleting the folder must never be a
        // way to delete the team's files.
        var withDelivery = await client.DeleteAsync($"/api/workspace/folders/{child.Id}");
        Assert.Equal(HttpStatusCode.Conflict, withDelivery.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.WorkspaceFolders.AsNoTracking().AnyAsync(f => f.Id == child.Id));
    }

    [Fact]
    public async Task Delete_RemovesAnEmptyFolder()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        var folder = await CreateFolderAsync(client, RootFolder);

        var response = await client.DeleteAsync($"/api/workspace/folders/{folder.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<FolderListDto>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Folders, f => f.Id == folder.Id);
    }

    [Fact]
    public async Task List_ReportsPerFolderSizeAndIsVisibleToEverySeat()
    {
        var (ownerClient, ownerId) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var folder = await CreateFolderAsync(ownerClient, RootFolder);

        await SeedDeliveryAsync(ownerId, workspace.Id, folder.Id, bytes: 4096);
        // Trashed rows are excluded: a folder's size should not count what is in the bin.
        await SeedDeliveryAsync(ownerId, workspace.Id, folder.Id, bytes: 999_999, deleted: true);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        await JoinAsync(memberClient, workspace.Id, "member@example.com");

        var seen = await memberClient.GetFromJsonAsync<FolderListDto>("/api/workspace/folders");

        Assert.NotNull(seen);
        var row = seen.Folders.Single(f => f.Id == folder.Id);
        Assert.Equal(1, row.Deliveries);
        Assert.Equal(4096, row.Bytes);
        // A plain member sees the tree but is not offered management.
        Assert.False(seen.CanManage);
    }

    [Fact]
    public async Task Create_IsRefusedToPlainMembers()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        await JoinAsync(memberClient, workspace.Id, "nope@example.com");

        var response = await memberClient.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = "Mine" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Defaults (#245) ──

    [Fact]
    public async Task Defaults_ResolveNearestFirstAndALockTravelsDown()
    {
        // Pure resolver: the inheritance rule is worth pinning without a workspace around it.
        var parent = new WorkspaceFolder
        {
            Name = "Client",
            DefaultExpiryHours = 720,
            DefaultPasswordRequired = true,
            DefaultPasswordHint = "shared vault",
            DefaultsLocked = true,
        };
        var child = new WorkspaceFolder
        {
            Name = "Invoices",
            DefaultExpiryHours = 24,
            DefaultPrivacyMode = true,
        };

        var resolved = FolderDefaults.Resolve([child, parent]);

        // Nearest folder with an opinion wins the expiry.
        Assert.Equal(24, resolved.ExpiryHours);
        // Protections only tighten: the parent's password requirement survives a silent child.
        Assert.True(resolved.PasswordRequired);
        Assert.Equal("shared vault", resolved.PasswordHint);
        Assert.True(resolved.PrivacyMode);
        // A lock set above cannot be escaped by uploading one level deeper.
        Assert.True(resolved.Locked);
    }

    [Fact]
    public async Task Defaults_AreSavedAndServedAsAPrefill()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        var folder = await CreateFolderAsync(client, RootFolder);

        var save = await client.PutAsJsonAsync($"/api/workspace/folders/{folder.Id}/defaults",
            new UpdateFolderDefaultsRequest
            {
                ExpiryHours = 48,
                PasswordRequired = true,
                PasswordHint = "the usual",
                PrivacyMode = true,
                Locked = true,
            });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var prefill = await client.GetFromJsonAsync<FolderPrefillDto>(
            $"/api/workspace/folders/{folder.Id}/prefill");

        Assert.NotNull(prefill);
        Assert.Equal(48, prefill.ExpiryHours);
        Assert.True(prefill.PasswordRequired);
        Assert.Equal("the usual", prefill.PasswordHint);
        Assert.True(prefill.PrivacyMode);
        Assert.True(prefill.Locked);
    }

    [Fact]
    public async Task Defaults_LockIsOwnerOnly()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        var folder = await CreateFolderAsync(ownerClient, RootFolder);

        var (adminClient, _) = await ClientAsync(PlanTierLimits.Free);
        await JoinAsync(adminClient, workspace.Id, "admin@example.com", WorkspaceRole.Admin);

        var response = await adminClient.PutAsJsonAsync($"/api/workspace/folders/{folder.Id}/defaults",
            new UpdateFolderDefaultsRequest { ExpiryHours = 12, Locked = true });

        // The admin may set defaults...
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WorkspaceFolders.AsNoTracking().SingleAsync(f => f.Id == folder.Id);
        Assert.Equal(12, stored.DefaultExpiryHours);
        // ...but turning them into a rule the whole team lives under stays with the owner.
        Assert.False(stored.DefaultsLocked);
    }

    [Fact]
    public async Task Defaults_OnAForeignFolderAreRefused()
    {
        var (firstClient, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(firstClient);
        var folder = await CreateFolderAsync(firstClient, RootFolder);

        var (otherClient, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(otherClient, name: "Other agency");

        var prefill = await otherClient.GetAsync($"/api/workspace/folders/{folder.Id}/prefill");

        Assert.Equal(HttpStatusCode.NotFound, prefill.StatusCode);
    }

    // ── Defaults at the commit boundary: prefill vs policy (#245) ──

    [Fact]
    public async Task Commit_IntoAnUnlockedFolder_LeavesTheSenderChoiceAlone()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        var folder = await CreateFolderAsync(client, RootFolder);

        await client.PutAsJsonAsync($"/api/workspace/folders/{folder.Id}/defaults",
            new UpdateFolderDefaultsRequest { ExpiryHours = 720, PrivacyMode = true, Locked = false });

        var deliveryId = await UploadAsync(client, folder.Id, expiryHours: 2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        Assert.Equal(folder.Id, delivery.FolderId);
        // An unlocked default is a prefill. The sender asked for two hours and got two hours —
        // a "default" that silently overrode them would be policy wearing a friendlier word.
        Assert.True(delivery.ExpiresAt < DateTime.UtcNow.AddHours(3));
        Assert.False(delivery.PrivacyMode);
    }

    [Fact]
    public async Task Commit_IntoALockedFolder_EnforcesTheDefaults()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        var folder = await CreateFolderAsync(client, RootFolder);

        await client.PutAsJsonAsync($"/api/workspace/folders/{folder.Id}/defaults",
            new UpdateFolderDefaultsRequest { ExpiryHours = 1, PrivacyMode = true, Locked = true });

        var deliveryId = await UploadAsync(client, folder.Id, expiryHours: 240);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);

        // Locked defaults are policy: the sender's ten days become the folder's one hour.
        Assert.True(delivery.ExpiresAt < DateTime.UtcNow.AddHours(2));
        Assert.True(delivery.PrivacyMode);
    }

    [Fact]
    public async Task Commit_IntoALockedFolderRequiringAPassword_IsRefusedWithoutOne()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        var folder = await CreateFolderAsync(client, RootFolder);

        await client.PutAsJsonAsync($"/api/workspace/folders/{folder.Id}/defaults",
            new UpdateFolderDefaultsRequest { PasswordRequired = true, Locked = true });

        // Refused, not silently generated — the server cannot invent a password and then have
        // nobody to tell it to.
        var refused = await CommitAsync(client, folder.Id, expiryHours: 24, password: null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var accepted = await CommitAsync(client, folder.Id, expiryHours: 24, password: "vault-2026");
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task Commit_IntoAFolderFromAnotherWorkspace_IsRefused()
    {
        var (firstClient, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(firstClient);
        var foreign = await CreateFolderAsync(firstClient, RootFolder);

        var (otherClient, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(otherClient, name: "Other agency 2");

        var response = await CommitAsync(otherClient, foreign.Id, expiryHours: 24, password: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Templates (#234) ──

    [Fact]
    public async Task Template_SnapshotsTheLayoutAndAppliesItUnderANewRoot()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);

        var root = await CreateFolderAsync(client, RootFolder);
        await CreateFolderAsync(client, "Invoices", root.Id);
        await client.PutAsJsonAsync($"/api/workspace/folders/{root.Id}/defaults",
            new UpdateFolderDefaultsRequest { ExpiryHours = 72, PasswordRequired = true });

        var save = await client.PostAsJsonAsync("/api/workspace/templates",
            new SaveTemplateRequest { Name = "Client project" });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var template = await save.Content.ReadFromJsonAsync<TemplateDto>();
        Assert.NotNull(template);
        Assert.Equal(2, template.FolderCount);

        var apply = await client.PostAsJsonAsync($"/api/workspace/templates/{template.Id}/apply",
            new ApplyTemplateRequest { RootName = "Acme" });
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<ApplyTemplateResultDto>();
        Assert.NotNull(applied);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acme = await db.WorkspaceFolders.AsNoTracking().SingleAsync(f => f.Name == "Acme");
        var copied = await db.WorkspaceFolders.AsNoTracking()
            .SingleAsync(f => f.ParentId == acme.Id && f.Name == RootFolder);

        // Settings ride along with the structure — that is the point of a template.
        Assert.Equal(72, copied.DefaultExpiryHours);
        Assert.True(copied.DefaultPasswordRequired);
        // Depth caps at two, so the template's own child cannot nest under the copied folder;
        // it is reported as skipped rather than failing the apply.
        Assert.True(applied.Skipped >= 1);
    }

    [Fact]
    public async Task Template_AppliedTwiceReusesByNameInsteadOfFailing()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        await CreateFolderAsync(client, RootFolder);

        var save = await client.PostAsJsonAsync("/api/workspace/templates",
            new SaveTemplateRequest { Name = "Layout" });
        var template = (await save.Content.ReadFromJsonAsync<TemplateDto>())!;

        var first = await client.PostAsJsonAsync($"/api/workspace/templates/{template.Id}/apply",
            new ApplyTemplateRequest { RootName = "Client A" });
        var firstResult = (await first.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!;
        Assert.Equal(2, firstResult.Created);

        var second = await client.PostAsJsonAsync($"/api/workspace/templates/{template.Id}/apply",
            new ApplyTemplateRequest { RootName = "Client A" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondResult = (await second.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!;

        // Nothing new, nothing broken — a re-run is safe.
        Assert.Equal(0, secondResult.Created);
        Assert.Equal(2, secondResult.Skipped);
    }

    [Fact]
    public async Task Template_DeleteLeavesTheFoldersItCreatedAlone()
    {
        var (client, _) = await ClientAsync(PlanTierLimits.Pro);
        await CreateWorkspaceAsync(client);
        await CreateFolderAsync(client, RootFolder);

        var save = await client.PostAsJsonAsync("/api/workspace/templates",
            new SaveTemplateRequest { Name = "Disposable" });
        var template = (await save.Content.ReadFromJsonAsync<TemplateDto>())!;

        await client.PostAsJsonAsync($"/api/workspace/templates/{template.Id}/apply",
            new ApplyTemplateRequest { RootName = "Kept" });

        var delete = await client.DeleteAsync($"/api/workspace/templates/{template.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.WorkspaceFolders.AsNoTracking().AnyAsync(f => f.Name == "Kept"));
        Assert.False(await db.WorkspaceFolderTemplates.AsNoTracking().AnyAsync(t => t.Id == template.Id));
    }

    [Fact]
    public async Task Template_IsRefusedToPlainMembers()
    {
        var (ownerClient, _) = await ClientAsync(PlanTierLimits.Pro);
        var workspace = await CreateWorkspaceAsync(ownerClient);
        await CreateFolderAsync(ownerClient, RootFolder);

        var (memberClient, _) = await ClientAsync(PlanTierLimits.Free);
        await JoinAsync(memberClient, workspace.Id, "member2@example.com");

        var response = await memberClient.PostAsJsonAsync("/api/workspace/templates",
            new SaveTemplateRequest { Name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> ClientAsync(string plan)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, plan);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<WorkspaceDto> CreateWorkspaceAsync(HttpClient client, string name = WorkspaceName)
    {
        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = name, Seats = 4 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/folders",
            new CreateFolderRequest { Name = name, ParentId = parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    /// <summary>Seeds an invite with a known token and accepts it, so the client holds a live seat.</summary>
    private async Task JoinAsync(
        HttpClient client, Guid workspaceId, string email, WorkspaceRole role = WorkspaceRole.Member)
    {
        var token = Guid.NewGuid().ToString("N");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                InviteEmail = email,
                Role = role,
                InviteTokenHash = WorkspaceService.HashToken(token),
                InviteExpiresAt = DateTime.UtcNow.AddDays(WorkspaceMember.InviteValidDays),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var accept = await client.PostAsJsonAsync("/api/workspace/invites/accept",
            new AcceptInviteRequest { Token = token });
        accept.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid folderId, double expiryHours)
    {
        var (response, deliveryId) = await CommitCoreAsync(client, folderId, expiryHours, password: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return deliveryId;
    }

    private static async Task<HttpResponseMessage> CommitAsync(
        HttpClient client, Guid folderId, double expiryHours, string? password)
        => (await CommitCoreAsync(client, folderId, expiryHours, password)).Response;

    private static async Task<(HttpResponseMessage Response, Guid DeliveryId)> CommitCoreAsync(
        HttpClient client, Guid folderId, double expiryHours, string? password)
    {
        const string FileName = "folder.bin";
        const string ContentType = "application/octet-stream";
        const long FileSize = 1024;

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
            ExpiryHours = expiryHours,
            Password = password,
            FolderId = folderId,
        });

        return (commit, start.DeliveryId);
    }

    private async Task SeedDeliveryAsync(
        Guid senderId, Guid workspaceId, Guid folderId, long bytes = 1024, bool deleted = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = senderId,
            WorkspaceId = workspaceId,
            FolderId = folderId,
            FileName = "asset.bin",
            ContentType = "application/octet-stream",
            FileSize = bytes,
            TotalBytes = bytes,
            ShareCode = Guid.NewGuid().ToString("N")[..12],
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            DeletedAt = deleted ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
    }
}
