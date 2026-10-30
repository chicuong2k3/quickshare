using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Upload presets (#100): Pro/Studio create, list, delete, and plan guard.
/// </summary>
public sealed class UploadPresetTests : IClassFixture<TestWebAppFactory>
{
    private const string Api = "/api/upload-presets";

    private readonly TestWebAppFactory _factory;

    public UploadPresetTests(TestWebAppFactory factory) => _factory = factory;

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<User> ProUserAsync()
    {
        var key = Guid.CreateVersion7().ToString();
        return await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
    }

    private sealed record PresetPayload(
        string Name,
        double? ExpiryHours = null,
        string? Password = null,
        bool BurnAfterDownload = false,
        bool PrivacyMode = false,
        string? Recipients = null);

    private sealed record PresetDto(
        Guid Id, string Name, double? ExpiryHours, string? Password,
        bool BurnAfterDownload, bool PrivacyMode, string? Recipients, DateTime CreatedAt);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreeUser_CreatePreset_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(TestWebAppFactory.TestUserProviderKey);
        var res = await client.PostAsJsonAsync(Api, new PresetPayload("Work docs"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task ProUser_CreatePreset_Returns200_WithAllFields()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        var res = await client.PostAsJsonAsync(Api, new PresetPayload(
            Name: "Client contract",
            ExpiryHours: 168,
            Password: "s3cret",
            BurnAfterDownload: true,
            PrivacyMode: false,
            Recipients: "bob@example.com, alice@example.com"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<PresetDto>();
        Assert.NotNull(dto);
        Assert.Equal("Client contract", dto.Name);
        Assert.Equal(168, dto.ExpiryHours);
        Assert.Equal("s3cret", dto.Password);
        Assert.True(dto.BurnAfterDownload);
        Assert.Contains("bob@example.com", dto.Recipients);
    }

    [Fact]
    public async Task ProUser_ListPresets_ReturnsCreatedPreset()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        await client.PostAsJsonAsync(Api, new PresetPayload("Alpha"));
        await client.PostAsJsonAsync(Api, new PresetPayload("Beta"));

        var list = await client.GetFromJsonAsync<List<PresetDto>>(Api);
        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.All(list, p => Assert.False(string.IsNullOrEmpty(p.Name)));
    }

    [Fact]
    public async Task ProUser_DeletePreset_Returns204_AndPresetGone()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        var created = await (await client.PostAsJsonAsync(Api, new PresetPayload("Temp")))
            .Content.ReadFromJsonAsync<PresetDto>();
        Assert.NotNull(created);

        var del = await client.DeleteAsync($"{Api}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await client.GetFromJsonAsync<List<PresetDto>>(Api);
        Assert.NotNull(list);
        Assert.DoesNotContain(list, p => p.Id == created.Id);
    }

    [Fact]
    public async Task DeletePreset_OwnedByOtherUser_Returns404()
    {
        var owner = await ProUserAsync();
        var other = await ProUserAsync();
        var ownerClient = _factory.CreateAuthenticatedClient(owner.Id.ToString());
        var otherClient = _factory.CreateAuthenticatedClient(other.Id.ToString());

        var created = await (await ownerClient.PostAsJsonAsync(Api, new PresetPayload("Secret")))
            .Content.ReadFromJsonAsync<PresetDto>();
        Assert.NotNull(created);

        var res = await otherClient.DeleteAsync($"{Api}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task CreatePreset_BlankName_Returns400()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());
        var res = await client.PostAsJsonAsync(Api, new PresetPayload(Name: "   "));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CreatePreset_AtLimit_Returns400()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        // Seed max directly to avoid slow serial HTTP calls.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < UploadPreset.MaxPresetsPerUser; i++)
        {
            db.UploadPresets.Add(new UploadPreset
            {
                Id = Guid.CreateVersion7(),
                OwnerId = user.Id,
                Name = $"Preset {i}",
            });
        }
        await db.SaveChangesAsync();

        var res = await client.PostAsJsonAsync(Api, new PresetPayload("One too many"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PresetsPage_Pro_Returns200()
    {
        var user = await ProUserAsync();
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());
        var res = await client.GetAsync(RoutePaths.AccountPresets);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
