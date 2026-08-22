using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Users.Sessions;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Session management (#105) and idle timeout / global sign-out (#173).
///
/// The point of the whole feature is one sentence: **a revoked session stops working on its next
/// request.** Before this, the auth cookie was self-contained — it proved who somebody was and
/// expired on its own schedule, and nothing on the server could take it back. A "sign out
/// everywhere" button over that would have been decoration.
///
/// So these tests are mostly about the negative direction: revoked, idle, and belonging to
/// somebody else all have to mean no.
/// </summary>
public sealed class SessionManagementTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public SessionManagementTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Session_IsValidWhileLiveAndInvalidOnceRevoked()
    {
        var userId = await SeedUserAsync();
        var session = await StartSessionAsync(userId);

        Assert.True(await ValidateAsync(session.SessionId));

        await RevokeAllAsync(userId, exceptSessionId: null);

        // The whole feature in one assertion: revocation takes effect on the next request, not
        // whenever the cookie happens to expire.
        Assert.False(await ValidateAsync(session.SessionId));
    }

    [Fact]
    public async Task Session_IsInvalidOnceItHasBeenIdleTooLong()
    {
        var userId = await SeedUserAsync();
        var session = await StartSessionAsync(userId);

        await SetLastSeenAsync(session.Id, DateTime.UtcNow - UserSession.IdleTimeout - TimeSpan.FromHours(1));

        // The cookie's own 30-day life asks "how long may somebody stay signed in". This asks
        // "how long after they walked away" — the shared-machine case is why both exist.
        Assert.False(await ValidateAsync(session.SessionId));
    }

    [Fact]
    public async Task Session_UnknownIdIsRefused()
    {
        Assert.False(await ValidateAsync("not-a-real-session-id"));
    }

    [Fact]
    public async Task Session_WithoutAClaimIsAllowedThrough()
    {
        // Cookies issued before this feature existed carry no session claim. Rejecting them would
        // sign out every logged-in user on deploy, to enforce a rule none of them had a chance to
        // satisfy. They expire on their own, and every new sign-in gets a session.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "legacy")], "TestAuth"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        Assert.True(await sessions.ValidateAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task Session_TouchIsThrottled()
    {
        var userId = await SeedUserAsync();
        var session = await StartSessionAsync(userId);

        // Fresh: validating must not write, or every page load becomes a database write.
        var before = await LastSeenAsync(session.Id);
        await ValidateAsync(session.SessionId);
        Assert.Equal(before, await LastSeenAsync(session.Id));

        // Stale past the interval: now it should be written.
        await SetLastSeenAsync(session.Id, DateTime.UtcNow - UserSession.TouchInterval - TimeSpan.FromMinutes(1));
        var stale = await LastSeenAsync(session.Id);
        await ValidateAsync(session.SessionId);
        Assert.True(await LastSeenAsync(session.Id) > stale);
    }

    [Fact]
    public async Task RevokeAll_KeepsTheCallersOwnSession()
    {
        var userId = await SeedUserAsync();
        var mine = await StartSessionAsync(userId);
        var laptop = await StartSessionAsync(userId);
        var phone = await StartSessionAsync(userId);

        var revoked = await RevokeAllAsync(userId, exceptSessionId: mine.SessionId);

        Assert.Equal(2, revoked);
        // "Sign out my other devices" is the actual ask. Signing yourself out too turns a
        // security action into a chore, and a chore is something people skip.
        Assert.True(await ValidateAsync(mine.SessionId));
        Assert.False(await ValidateAsync(laptop.SessionId));
        Assert.False(await ValidateAsync(phone.SessionId));
    }

    [Fact]
    public async Task RevokeAll_WithNoExceptionCutsEverything()
    {
        var userId = await SeedUserAsync();
        var first = await StartSessionAsync(userId);
        var second = await StartSessionAsync(userId);

        // This is the offboarding shape (#146): SCIM has no current session to preserve.
        await RevokeAllAsync(userId, exceptSessionId: null);

        Assert.False(await ValidateAsync(first.SessionId));
        Assert.False(await ValidateAsync(second.SessionId));
    }

    [Fact]
    public async Task Revoke_CannotTouchAnotherAccountsSession()
    {
        var ownerId = await SeedUserAsync();
        var strangerId = await SeedUserAsync();
        var session = await StartSessionAsync(ownerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        // Scoped by user, never by id alone.
        Assert.False(await sessions.RevokeAsync(strangerId, session.Id, CancellationToken.None));
        Assert.True(await ValidateAsync(session.SessionId));
    }

    [Fact]
    public async Task ScimDeprovision_CutsTheMembersSessions()
    {
        var (admin, workspaceId) = await WorkspaceClientAsync();
        var email = $"leaver-{Guid.NewGuid():N}@acme.example";

        var memberId = await SeedUserAsync(email);
        var session = await StartSessionAsync(memberId);

        var scim = await ScimClientAsync(admin);
        var created = await scim.PostAsJsonAsync("/scim/v2/Users", new { userName = email, active = true });
        created.EnsureSuccessStatusCode();
        var scimId = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var patched = await scim.PatchAsync($"/scim/v2/Users/{scimId}", JsonBody(new
        {
            Operations = new object[] { new { op = "replace", path = "active", value = false } },
        }));
        patched.EnsureSuccessStatusCode();

        // This was a documented gap when SCIM shipped: the seat died at once but a signed-in
        // browser kept working until its cookie expired, which is not what "IT deactivated them"
        // is supposed to mean.
        Assert.False(await ValidateAsync(session.SessionId));
        _ = workspaceId;
    }

    [Fact]
    public async Task SecurityPage_ListsSessionsAndOffersGlobalSignOut()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        await StartSessionAsync(user.Id);
        await StartSessionAsync(user.Id);

        var client = _factory.CreateAuthenticatedClient(providerKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountSecurity);
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("security.session.revoke", html, StringComparison.Ordinal);
        Assert.Contains("security.session.revoke-all", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionsApi_ListsOnlyTheCallersOwn()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        await StartSessionAsync(user.Id);

        var strangerId = await SeedUserAsync();
        await StartSessionAsync(strangerId);

        var client = _factory.CreateAuthenticatedClient(providerKey);
        var listed = await client.GetFromJsonAsync<List<SessionDto>>("/api/account/sessions");

        Assert.Single(listed!);
    }

    // ── Helpers ──

    private static StringContent JsonBody(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

    private async Task<Guid> SeedUserAsync(string? email = null)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        if (email is not null)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = email;
            await db.SaveChangesAsync();
        }

        return user.Id;
    }

    private async Task<UserSession> StartSessionAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return await sessions.StartAsync(userId, new DefaultHttpContext(), CancellationToken.None);
    }

    private async Task<bool> ValidateAsync(string sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SessionService.SessionClaimType, sessionId)], "TestAuth"));

        return await sessions.ValidateAsync(principal, CancellationToken.None);
    }

    private async Task<int> RevokeAllAsync(Guid userId, string? exceptSessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return await sessions.RevokeAllAsync(userId, exceptSessionId, CancellationToken.None);
    }

    private async Task SetLastSeenAsync(Guid sessionRowId, DateTime lastSeen)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions.FirstAsync(s => s.Id == sessionRowId);
        session.LastSeenAt = lastSeen;
        await db.SaveChangesAsync();
    }

    private async Task<DateTime> LastSeenAsync(Guid sessionRowId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.AsNoTracking()
            .Where(s => s.Id == sessionRowId).Select(s => s.LastSeenAt).FirstAsync();
    }

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new QuickShare.Features.Workspaces.Manage.CreateWorkspaceRequest
            {
                Name = $"S {Guid.NewGuid():N}"[..14],
                Seats = 5,
            });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<QuickShare.Features.Workspaces.Manage.WorkspaceDto>())!;

        return (client, workspace.Id);
    }

    private async Task<HttpClient> ScimClientAsync(HttpClient admin)
    {
        var issued = await admin.PostAsJsonAsync("/api/workspace/scim-tokens", new { label = "Okta" });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<QuickShare.Features.Auth.Sso.ScimTokenDto>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }
}
