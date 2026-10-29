using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class WorkspaceFolderNativeFormTests : IClassFixture<TestWebAppFactory>
{
    private const string FormName = "workspace-folder-create";
    private readonly TestWebAppFactory _factory;

    public WorkspaceFolderNativeFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_RequiresAntiforgeryAndPreservesWorkspaceRules()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Pro);
        var foreignOwner = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString(), PlanTierLimits.Pro);
        var workspaceId = Guid.CreateVersion7();
        var foreignWorkspaceId = Guid.CreateVersion7();
        var foreignFolderId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Id = workspaceId, OwnerId = owner.Id, Name = "Native folders" });
            db.Workspaces.Add(new Workspace { Id = foreignWorkspaceId, OwnerId = foreignOwner.Id, Name = "Other folders" });
            db.WorkspaceFolders.Add(new WorkspaceFolder
            {
                Id = foreignFolderId,
                WorkspaceId = foreignWorkspaceId,
                Name = "Foreign",
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = owner.Id,
                InviteEmail = owner.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = Client(ownerKey);
        var page = await client.GetStringAsync(RoutePaths.AccountWorkspaceFolders);
        var token = AntiforgeryHelper.ExtractToken(page);
        using (var missing = Form("Alpha", string.Empty, null))
        using (var rejected = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, missing))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using (var form = Form("Alpha", string.Empty, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, form))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(RoutePaths.AccountWorkspaceFolders, response.Headers.Location?.OriginalString);
        }

        using (var duplicate = Form("Alpha", string.Empty, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, duplicate))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"Alpha\"", html, StringComparison.Ordinal);
        }

        using (var malformed = Form("Child", "invalid", token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, malformed))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var foreign = Form("Child", foreignFolderId.ToString(), token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, foreign))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"unavailable\" selected", html, StringComparison.Ordinal);
        }

        using (var retry = Form("Child", "unavailable", token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, retry))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"unavailable\" selected", html, StringComparison.Ordinal);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folders = await verifyDb.WorkspaceFolders.AsNoTracking().Where(f => f.WorkspaceId == workspaceId).ToListAsync();
        Assert.Single(folders);
        Assert.Equal("Alpha", folders[0].Name);
    }

    [Fact]
    public async Task RenameAndDelete_UseIndependentFormsAndKeepNonemptyFolders()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Pro);
        var workspaceId = Guid.CreateVersion7();
        var parentId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var siblingId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Id = workspaceId, OwnerId = owner.Id, Name = "Folder actions" });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = owner.Id,
                InviteEmail = owner.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = parentId, WorkspaceId = workspaceId, Name = "Parent" });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = childId, WorkspaceId = workspaceId, ParentId = parentId, Name = "Child" });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = siblingId, WorkspaceId = workspaceId, Name = "Existing" });
            await db.SaveChangesAsync();
        }

        using var client = Client(ownerKey);
        var page = await client.GetStringAsync(RoutePaths.AccountWorkspaceFolders);
        var token = AntiforgeryHelper.ExtractToken(page);
        var renameParent = $"workspace-folder-rename-{parentId:N}";
        var deleteParent = $"workspace-folder-delete-{parentId:N}";
        var renameChild = $"workspace-folder-rename-{childId:N}";
        var deleteChild = $"workspace-folder-delete-{childId:N}";
        Assert.Contains(renameParent, page, StringComparison.Ordinal);
        Assert.Contains(renameChild, page, StringComparison.Ordinal);
        Assert.Contains(deleteParent, page, StringComparison.Ordinal);
        Assert.Contains(deleteChild, page, StringComparison.Ordinal);

        using (var missing = ActionForm(renameParent, "Changed", null))
        using (var rejected = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, missing))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using (var duplicate = ActionForm(renameParent, "Existing", token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, duplicate))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"ws-folder-name\" name=\"createFolderName\" value=\"Existing\"", html, StringComparison.Ordinal);
        }

        using (var rename = ActionForm(renameParent, "Changed", token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, rename))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using (var delete = ActionForm(deleteParent, null, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, delete))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var delete = ActionForm(deleteChild, null, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, delete))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folders = await verifyDb.WorkspaceFolders.AsNoTracking().Where(f => f.WorkspaceId == workspaceId).ToListAsync();
        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, folder => folder.Id == parentId && folder.Name == "Changed");
        Assert.Contains(folders, folder => folder.Id == siblingId && folder.Name == "Existing");
    }

    [Fact]
    public async Task Move_RequiresAntiforgeryAndReordersOnlySiblings()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Pro);
        var workspaceId = Guid.CreateVersion7();
        var firstRoot = Guid.CreateVersion7();
        var secondRoot = Guid.CreateVersion7();
        var firstChild = Guid.CreateVersion7();
        var secondChild = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Workspaces.Add(new Workspace { Id = workspaceId, OwnerId = owner.Id, Name = "Folder order" });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                UserId = owner.Id,
                InviteEmail = owner.Email,
                Role = WorkspaceRole.Owner,
                JoinedAt = DateTime.UtcNow,
            });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = firstRoot, WorkspaceId = workspaceId, Name = "Alpha" });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = secondRoot, WorkspaceId = workspaceId, Name = "Beta" });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = firstChild, WorkspaceId = workspaceId, ParentId = firstRoot, Name = "Child A" });
            db.WorkspaceFolders.Add(new WorkspaceFolder { Id = secondChild, WorkspaceId = workspaceId, ParentId = firstRoot, Name = "Child B" });
            await db.SaveChangesAsync();
        }

        using var client = Client(ownerKey);
        var page = await client.GetStringAsync(RoutePaths.AccountWorkspaceFolders);
        var token = AntiforgeryHelper.ExtractToken(page);
        var moveRoot = $"workspace-folder-move-Up-{secondRoot:N}";
        var moveChild = $"workspace-folder-move-Up-{secondChild:N}";
        using (var missing = ActionForm(moveRoot, null, null))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, missing))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var form = ActionForm(moveRoot, null, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, form))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using (var form = ActionForm(moveChild, null, token))
        using (var response = await client.PostAsync(RoutePaths.AccountWorkspaceFolders, form))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folders = await verifyDb.WorkspaceFolders.AsNoTracking().Where(f => f.WorkspaceId == workspaceId).ToListAsync();
        var roots = folders.Where(f => f.ParentId is null).OrderBy(f => f.SortOrder).Select(f => f.Id).ToArray();
        var children = folders.Where(f => f.ParentId == firstRoot).OrderBy(f => f.SortOrder).Select(f => f.Id).ToArray();
        Assert.Equal([secondRoot, firstRoot], roots);
        Assert.Equal([secondChild, firstChild], children);
    }

    private HttpClient Client(string key)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");
        return client;
    }

    private static FormUrlEncodedContent Form(string name, string parent, string? token)
    {
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = FormName,
            ["createFolderName"] = name,
            ["folderParent"] = parent,
        };
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }
        return new FormUrlEncodedContent(fields);
    }

    private static FormUrlEncodedContent ActionForm(string handler, string? name, string? token)
    {
        var fields = new Dictionary<string, string> { ["_handler"] = handler };
        if (name is not null)
        {
            var field = handler.Replace("workspace-folder-rename-", "renameFolderName-", StringComparison.Ordinal);
            fields[field] = name;
        }
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }
        return new FormUrlEncodedContent(fields);
    }
}
