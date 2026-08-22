using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Developer;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Per-key usage (#273).
///
/// The question a developer actually asks is "is my integration still running, and how hard" —
/// a daily bucket answers it, a row per request would be a log with retention and privacy
/// questions attached. What is asserted here is that calls are counted, that the counting
/// survives the buffer, and that a call-volume graph is the owner's and nobody else's.
/// </summary>
/// <remarks>
/// Shares the API-key fixture rather than standing up a second application: every fixture is a
/// whole host, and two of them for one subject is a cost paid on every run.
/// </remarks>
[Collection(ApiKeyTestGroup.Name)]
public sealed class ApiKeyUsageTests
{
    private readonly TestWebAppFactory _factory;

    public ApiKeyUsageTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Calls_AreCountedOnceFlushed()
    {
        var (owner, key, keyId) = await KeyAsync();

        await CallAsync(key);
        await CallAsync(key);
        await FlushAsync();

        var usage = await UsageAsync(owner, keyId);

        var today = Assert.Single(usage);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), today.Day);
        Assert.True(today.Requests >= 2);
    }

    [Fact]
    public async Task Flushing_AddsToWhatIsAlreadyThere()
    {
        var (owner, key, keyId) = await KeyAsync();

        await CallAsync(key);
        await FlushAsync();
        await CallAsync(key);
        await FlushAsync();

        // Two instances flushing the same day must sum rather than race to be last.
        var usage = await UsageAsync(owner, keyId);
        Assert.Equal(2, Assert.Single(usage).Requests);
    }

    [Fact]
    public async Task AKeyWithNoCalls_ReportsNothingRatherThanZeroes()
    {
        var (owner, _, keyId) = await KeyAsync();

        await FlushAsync();

        // Days with no calls are absent, not zero rows — the graph is about what happened.
        Assert.Empty(await UsageAsync(owner, keyId));
    }

    [Fact]
    public async Task Usage_IsTheOwnersAndNobodyElses()
    {
        var (_, _, keyId) = await KeyAsync();
        var (stranger, _, _) = await KeyAsync();

        var response = await stranger.GetAsync($"/api/developer/keys/{keyId}/usage");

        // A call-volume graph says when a business is busy. That is not a fact to hand to
        // whoever guesses a key id.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeveloperPage_OffersTheUsageControl()
    {
        var (owner, _, _) = await KeyAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/developer");
        request.Headers.Add("HX-Request", "true");
        var html = await (await owner.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("dev.key.usage", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Owner, string RawKey, Guid KeyId)> KeyAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/developer/keys",
            new CreateApiKeyRequest { Name = "metered", Scopes = ApiScopes.Read });
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<ApiKeyDto>())!;

        return (client, dto.Key!, dto.Id);
    }

    private async Task CallAsync(string rawKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/deliveries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);

        var response = await _factory.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task FlushAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiKeyUsageFlushJob>().FlushAsync();
    }

    private static async Task<List<ApiKeyUsageDto>> UsageAsync(HttpClient owner, Guid keyId)
    {
        var response = await owner.GetAsync($"/api/developer/keys/{keyId}/usage");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<ApiKeyUsageDto>>())!;
    }
}
