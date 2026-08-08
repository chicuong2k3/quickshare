using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

public sealed class FolderFileApiTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public FolderFileApiTests(TestWebAppFactory factory) => _factory = factory;

    // Folder names are globally unique and the in-memory store is shared
    // across parallel test classes — always use random names.
    private static string UniqueName() => $"folder-{Guid.NewGuid():N}";

    [Fact]
    public async Task CreateFolder_ReturnsFolder_And_DuplicateReturns409()
    {
        var client = _factory.CreateAuthenticatedClient();
        var name = UniqueName();

        var created = await client.PostAsJsonAsync("/api/folders", new { Name = name });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync();
        Assert.Contains(name, body);

        var duplicate = await client.PostAsJsonAsync("/api/folders", new { Name = name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/folders", new { Name = UniqueName() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_UnknownId_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync($"/api/folders/{Guid.NewGuid()}", new { Name = UniqueName() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_RenameToExistingName_Returns409()
    {
        var client = _factory.CreateAuthenticatedClient();
        var takenName = UniqueName();
        await client.PostAsJsonAsync("/api/folders", new { Name = takenName });
        var folderId = await SeedFolderAsync(UniqueName());

        var response = await client.PutAsJsonAsync($"/api/folders/{folderId}", new { Name = takenName });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_Rename_Succeeds()
    {
        var client = _factory.CreateAuthenticatedClient();
        var folderId = await SeedFolderAsync(UniqueName());
        var newName = UniqueName();

        var response = await client.PutAsJsonAsync($"/api/folders/{folderId}", new { Name = newName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(newName, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteFolder_EmptyFolder_Returns204()
    {
        var client = _factory.CreateAuthenticatedClient();
        var folderId = await SeedFolderAsync(UniqueName());

        var response = await client.DeleteAsync($"/api/folders/{folderId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_WithFiles_Returns409()
    {
        var client = _factory.CreateAuthenticatedClient();
        var folderId = await SeedFolderAsync(UniqueName());
        await SeedFileAsync(folderId, _factory.TestUserId);

        var response = await client.DeleteAsync($"/api/folders/{folderId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_UnknownId_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.DeleteAsync($"/api/folders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveFile_UnknownFile_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient();
        var folderId = await SeedFolderAsync(UniqueName());
        var response = await client.PatchAsJsonAsync(
            $"/api/files/{Guid.NewGuid()}/folder", new { TargetFolderId = folderId });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveFile_NotOwned_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient();
        var folderId = await SeedFolderAsync(UniqueName());
        var otherUsersFileId = await SeedFileAsync(folderId, ownerId: Guid.NewGuid());

        var response = await client.PatchAsJsonAsync(
            $"/api/files/{otherUsersFileId}/folder", new { TargetFolderId = folderId });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoveFile_ToAnotherFolder_Succeeds()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sourceFolderId = await SeedFolderAsync(UniqueName());
        var targetFolderId = await SeedFolderAsync(UniqueName());
        var fileId = await SeedFileAsync(sourceFolderId, _factory.TestUserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/files/{fileId}/folder", new { TargetFolderId = targetFolderId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(targetFolderId.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Avatar_Authenticated_ReturnsInitialsHtml()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/me/avatar");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // Seeded user is "Test User" → initials TU.
        Assert.Contains("TU", html);
    }

    private async Task<Guid> SeedFolderAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folder = new Folder { Id = Guid.CreateVersion7(), OwnerId = _factory.TestUserId, Name = name };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<Guid> SeedFileAsync(Guid folderId, Guid? ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = new FileMetadata
        {
            Id = Guid.CreateVersion7(),
            Filename = "seeded.bin",
            ContentType = "application/octet-stream",
            Size = 42,
            FolderId = folderId,
            OwnerId = ownerId,
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }
}
