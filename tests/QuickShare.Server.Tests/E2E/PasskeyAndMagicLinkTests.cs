using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Auth.MagicLink;
using QuickShare.Features.Auth.Passkeys;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Passkeys (#68) and magic-link sign-in (#132).
///
/// The WebAuthn ceremony itself needs a real authenticator, so what is tested here is everything
/// around it — which is where the mistakes that matter live:
///
/// - **A challenge is single-use.** One that survives its ceremony is a replay waiting to happen.
/// - **A magic link is single-use and short-lived**, because it sits in an inbox and mail gateways
///   follow links.
/// - **Neither endpoint says whether an address has an account.** A sign-in form that answers that
///   question is an account-existence oracle for anybody with a list of addresses.
/// </summary>
public sealed class PasskeyAndMagicLinkTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PasskeyAndMagicLinkTests(TestWebAppFactory factory) => _factory = factory;

    // ── #68 passkeys ──

    [Fact]
    public async Task Passkey_RegistrationBeginIssuesAChallenge()
    {
        var (client, userId) = await AccountAsync();

        var response = await client.PostAsync("/api/account/passkeys/register/begin", null);
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = await db.PasskeyChallenges.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId);

        Assert.NotNull(challenge);
        // Server-generated and time-boxed: a client-chosen challenge is not a challenge.
        Assert.True(challenge.ExpiresAt > DateTime.UtcNow);
        Assert.False(string.IsNullOrEmpty(challenge.OptionsJson));
    }

    [Fact]
    public async Task Passkey_LoginBeginIsAnonymousAndCarriesNoCredentialIds()
    {
        var response = await _factory.CreateClient().PostAsync("/api/auth/passkey/begin", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        // Usernameless by design. Returning the credential ids for an address would make this an
        // account-existence oracle, and the discoverable-credential flow does not need them.
        Assert.DoesNotContain("allowCredentials\":[{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Passkey_ChallengeIsConsumedByTheFirstAttempt()
    {
        var (client, userId) = await AccountAsync();

        var begin = await client.PostAsync("/api/account/passkeys/register/begin", null);
        var handle = (await begin.Content.ReadFromJsonAsync<CeremonyProbe>())!.Handle;

        await using var scope = _factory.Services.CreateAsyncScope();
        var passkeys = scope.ServiceProvider.GetRequiredService<PasskeyService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);

        // A junk attestation still spends the challenge: the consume happens before verification,
        // so a failed attempt cannot be retried against the same challenge. A challenge that
        // survives its ceremony is a replay waiting to be noticed by somebody else.
        await Assert.ThrowsAnyAsync<Exception>(() => passkeys.CompleteRegistrationAsync(
            user, handle, new Fido2NetLib.AuthenticatorAttestationRawResponse(), null, CancellationToken.None));

        Assert.False(await db.PasskeyChallenges.AsNoTracking().AnyAsync(c => c.Handle == handle));
    }

    [Fact]
    public async Task Passkey_CompleteWithAnUnknownHandleIsRefused()
    {
        var (client, _) = await AccountAsync();

        var response = await client.PostAsJsonAsync("/api/account/passkeys/register/complete",
            new { handle = "not-a-real-handle", response = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Passkey_ListAndRemoveAreScopedToTheOwner()
    {
        var (owner, ownerId) = await AccountAsync();
        var (stranger, _) = await AccountAsync();

        var passkeyId = await SeedPasskeyAsync(ownerId);

        var listed = await owner.GetFromJsonAsync<List<PasskeyDto>>("/api/account/passkeys");
        Assert.Single(listed!);

        // Scoped by account, never by id alone.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/account/passkeys/{passkeyId}")).StatusCode);
        (await owner.DeleteAsync($"/api/account/passkeys/{passkeyId}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Passkey_SignInWithAnUnknownCredentialIsRefusedWithoutDetail()
    {
        var begin = await _factory.CreateClient().PostAsync("/api/auth/passkey/begin", null);
        var handle = (await begin.Content.ReadFromJsonAsync<CeremonyProbe>())!.Handle;

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/passkey/complete",
            new { handle, response = (object?)null });

        // One error for every failure: the difference between "no such credential" and "bad
        // signature" is information about which credential ids exist.
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SecurityPage_OffersPasskeyRegistration()
    {
        var (client, _) = await AccountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountSecurity);
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("security.passkey.register", html, StringComparison.Ordinal);
    }

    // ── #132 magic link ──

    [Fact]
    public async Task MagicLink_AlwaysReturnsTheSameAnswer()
    {
        var (_, userId) = await AccountAsync();
        var known = await EmailOfAsync(userId);

        var forKnown = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/magic-link", new { email = known });
        var forUnknown = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/magic-link", new { email = $"nobody-{Guid.NewGuid():N}@example.com" });

        // If these ever differ, the endpoint tells anybody with a list of addresses which of them
        // have accounts here.
        Assert.Equal(forKnown.StatusCode, forUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, forKnown.StatusCode);
    }

    [Fact]
    public async Task MagicLink_IssuesATokenOnlyForARealAccount()
    {
        var (_, userId) = await AccountAsync();
        var known = await EmailOfAsync(userId);
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";

        await RequestLinkAsync(known);
        await RequestLinkAsync(unknown);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await db.LoginTokens.AsNoTracking().AnyAsync(t => t.Email == known));
        // Silent, and no account created: a sign-in form must not double as a registration form
        // anybody can point at somebody else's address.
        Assert.False(await db.LoginTokens.AsNoTracking().AnyAsync(t => t.Email == unknown));
        Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.Email == unknown));
    }

    [Fact]
    public async Task MagicLink_TokenIsSingleUse()
    {
        var (_, userId) = await AccountAsync();
        var email = await EmailOfAsync(userId);
        var raw = await IssueTokenAsync(email);

        Assert.NotNull(await ConsumeAsync(raw));
        // Mail gateways follow links. One of those must not consume the human's only chance
        // without them noticing — so a second use fails rather than silently signing somebody in.
        Assert.Null(await ConsumeAsync(raw));
    }

    [Fact]
    public async Task MagicLink_ExpiredTokenIsRefused()
    {
        var (_, userId) = await AccountAsync();
        var email = await EmailOfAsync(userId);
        var raw = await IssueTokenAsync(email, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        Assert.Null(await ConsumeAsync(raw));
    }

    [Fact]
    public async Task MagicLink_TokenIsStoredHashed()
    {
        var (_, userId) = await AccountAsync();
        var email = await EmailOfAsync(userId);
        var raw = await IssueTokenAsync(email);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The raw token exists in one email and nowhere else.
        Assert.False(await db.LoginTokens.AsNoTracking().AnyAsync(t => t.TokenHash == raw));
        Assert.True(await db.LoginTokens.AsNoTracking().AnyAsync(t => t.TokenHash == WorkspaceService.HashToken(raw)));
    }

    [Fact]
    public async Task MagicLink_LandingRejectsAJunkToken()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync($"{RoutePaths.MagicLink}?token=nonsense");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(RoutePaths.Login, response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPage_OffersBothNewSignInMethods()
    {
        var html = await _factory.CreateClient().GetStringAsync(RoutePaths.Login);

        // Without these the endpoints are an API nobody can reach.
        Assert.Contains("login.passkey", html, StringComparison.Ordinal);
        Assert.Contains("login.magiclink", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private sealed record CeremonyProbe(string Handle);

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.FirstAsync(u => u.Id == user.Id);
        row.Email = $"user-{Guid.NewGuid():N}@example.com";
        await db.SaveChangesAsync();

        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstAsync();
    }

    private async Task RequestLinkAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var links = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
        await links.RequestAsync(email, CancellationToken.None);
    }

    /// <summary>Writes a token directly, so the raw value is known without reading an inbox.</summary>
    private async Task<string> IssueTokenAsync(string email, DateTime? expiresAt = null)
    {
        var raw = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            TokenHash = WorkspaceService.HashToken(raw),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.Add(LoginToken.Lifetime),
        });
        await db.SaveChangesAsync();

        return raw;
    }

    private async Task<User?> ConsumeAsync(string raw)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var links = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
        return await links.ConsumeAsync(raw, CancellationToken.None);
    }

    private async Task<Guid> SeedPasskeyAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var passkey = new UserPasskey
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CredentialId = Guid.NewGuid().ToString("N"),
            PublicKey = Convert.ToBase64String(new byte[32]),
        };
        db.UserPasskeys.Add(passkey);
        await db.SaveChangesAsync();

        return passkey.Id;
    }
}
