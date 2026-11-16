using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// E2E tests for the admin feature-flag API: auth gating, listing, and overrides.
/// The factory disables FusionCache, so every request hits the DB — persistence
/// asserts are meaningful without cache-invalidation noise.
/// </summary>
public sealed class AdminFlagsApiTests : IClassFixture<TestWebAppFactory>
{
    private const string NonAdminEmail = "nobody@example.com";

    private readonly TestWebAppFactory _factory;

    public AdminFlagsApiTests(TestWebAppFactory factory) => _factory = factory;

    // ── Auth gating ──

    [Fact]
    public async Task ListFlags_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/flags");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListFlags_AuthenticatedNonAdmin_Returns403()
    {
        var client = CreateNonAdminClient();
        var response = await client.GetAsync("/api/admin/flags");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFlag_AuthenticatedNonAdmin_Returns403()
    {
        var client = CreateNonAdminClient();
        var response = await client.PutAsJsonAsync("/api/admin/flags/previews", new { enabled = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Listing ──

    [Fact]
    public async Task ListFlags_Admin_ReturnsAllConfiguredFlags()
    {
        var client = _factory.CreateAuthenticatedClient();
        var flags = await client.GetFromJsonAsync<List<FeatureFlagStatus>>("/api/admin/flags");

        Assert.NotNull(flags);
        Assert.Equal(4, flags.Count);

        var keys = flags.Select(f => f.Key).ToHashSet();
        Assert.Contains(FeatureFlagKeys.Previews, keys);
        Assert.Contains(FeatureFlagKeys.ZeroKnowledge, keys);
        Assert.Contains(FeatureFlagKeys.PasswordProtection, keys);
        Assert.Contains(FeatureFlagKeys.VirusScan, keys);

        // password-protection ships ON by default and no test overrides it.
        var passwordProtection = flags.Single(f => f.Key == FeatureFlagKeys.PasswordProtection);
        Assert.True(passwordProtection.DefaultEnabled);
        Assert.True(passwordProtection.EffectiveEnabled);

        // previews ships OFF by default (config-level assertion, override-agnostic).
        var previews = flags.Single(f => f.Key == FeatureFlagKeys.Previews);
        Assert.False(previews.DefaultEnabled);
    }

    // ── Overrides ──

    [Fact]
    public async Task UpdateFlag_Admin_ValidOverride_PersistsAndRestores()
    {
        var client = _factory.CreateAuthenticatedClient();

        var put = await client.PutAsJsonAsync("/api/admin/flags/zero-knowledge", new { enabled = true, rolloutPercent = 100 });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        // Override is visible in the API read-model.
        var flags = await client.GetFromJsonAsync<List<FeatureFlagStatus>>("/api/admin/flags");
        var zeroKnowledge = flags!.Single(f => f.Key == FeatureFlagKeys.ZeroKnowledge);
        Assert.True(zeroKnowledge.OverrideEnabled);
        Assert.Equal(100, zeroKnowledge.OverrideRolloutPercent);
        Assert.True(zeroKnowledge.EffectiveEnabled);

        // Override is visible to the service (DB row exists, not just response caching).
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
        Assert.True(await service.IsEnabledAsync(FeatureFlagKeys.ZeroKnowledge, _factory.TestUserId));

        // Restore the baseline so sibling tests sharing this factory see a clean DB.
        var restore = await client.PutAsJsonAsync("/api/admin/flags/zero-knowledge", new { enabled = false, rolloutPercent = 100 });
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
    }

    [Fact]
    public async Task UpdateFlag_UnknownKey_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync("/api/admin/flags/not-a-real-flag", new { enabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFlag_InvalidRolloutPercent_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync("/api/admin/flags/previews", new { enabled = true, rolloutPercent = 150 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Page smoke tests ──

    [Fact]
    public async Task AdminFlagsPage_Anonymous_RedirectsToLogin()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/flags");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AdminFlagsPage_Admin_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/admin/flags");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateNonAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, NonAdminEmail);
        return client;
    }
}