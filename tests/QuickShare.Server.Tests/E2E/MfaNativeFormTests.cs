using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Auth.Totp;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class MfaNativeFormTests : IClassFixture<TestWebAppFactory>
{
    private const int ChallengeAttemptLimit = 10;
    private readonly TestWebAppFactory _factory;

    public MfaNativeFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ChallengeForm_ValidatesAntiforgeryAndSpendsRecoveryCodeOnce()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        var (codes, hashes) = TotpService.GenerateRecoveryCodes();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserTotps.Add(new UserTotp
            {
                UserId = user.Id,
                Secret = TotpService.GenerateSecret(),
                ConfirmedAt = DateTime.UtcNow,
                RecoveryCodeHashes = hashes,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        var html = await client.GetStringAsync(RoutePaths.MfaChallenge);
        Assert.Contains("name=\"code\"", html);
        using (var styles = await client.GetAsync(RoutePaths.ScopedStyles))
        {
            Assert.Equal(HttpStatusCode.OK, styles.StatusCode);
            Assert.Equal("text/css", styles.Content.Headers.ContentType?.MediaType);
        }

        using (var missingToken = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "mfa-challenge",
            ["code"] = codes[0],
        }))
        {
            using var response = await client.PostAsync(RoutePaths.MfaChallenge, missingToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var invalid = Form(html, "invalid"))
        {
            using var response = await client.PostAsync(RoutePaths.MfaChallenge, invalid);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync());
        }

        using (var valid = Form(html, codes[0]))
        {
            using var response = await client.PostAsync(RoutePaths.MfaChallenge, valid);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(RoutePaths.Home, response.Headers.Location?.OriginalString);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains(AuthConstants.CookieName));
        }

        using (var replay = Form(html, codes[0]))
        {
            using var response = await client.PostAsync(RoutePaths.MfaChallenge, replay);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("role=\"alert\"", await response.Content.ReadAsStringAsync());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var totp = await verifyDb.UserTotps.AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(UserTotp.RecoveryCodeCount - 1, TotpService.CountRecoveryCodes(totp.RecoveryCodeHashes));
    }

    private static FormUrlEncodedContent Form(string html, string code) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
        ["_handler"] = "mfa-challenge",
        ["code"] = code,
    });

    [Fact]
    public async Task ApiChallenge_StillAcceptsTotpAndRejectsReplay()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        var secret = TotpService.GenerateSecret();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserTotps.Add(new UserTotp
            {
                UserId = user.Id,
                Secret = secret,
                ConfirmedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateUnverifiedClient(key);
        var code = TotpService.ComputeCode(secret, TotpService.StepFor(DateTime.UtcNow));
        using var first = await client.PostAsJsonAsync("/api/account/totp/verify", new { code });
        using var replay = await client.PostAsJsonAsync("/api/account/totp/verify", new { code });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ApiAndForm_ShareAccountLimitEvenWhenUserAgentChanges()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        var (codes, hashes) = TotpService.GenerateRecoveryCodes();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserTotps.Add(new UserTotp
            {
                UserId = user.Id,
                Secret = TotpService.GenerateSecret(),
                ConfirmedAt = DateTime.UtcNow,
                RecoveryCodeHashes = hashes,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateUnverifiedClient(key);
        var html = await client.GetStringAsync(RoutePaths.MfaChallenge);
        for (var i = 0; i < ChallengeAttemptLimit; i++)
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"test-agent-{i}");
            using var response = await client.PostAsJsonAsync("/api/account/totp/verify", new { code = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("test-agent-form");
        using var form = Form(html, codes[0]);
        using var blocked = await client.PostAsync(RoutePaths.MfaChallenge, form);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        Assert.Contains("Too many attempts", await blocked.Content.ReadAsStringAsync());

        using var disable = new HttpRequestMessage(HttpMethod.Delete, "/api/account/totp")
        {
            Content = JsonContent.Create(new { code = codes[0] }),
        };
        using var disabled = await client.SendAsync(disable);
        Assert.Equal(HttpStatusCode.TooManyRequests, disabled.StatusCode);

        using var confirmed = await client.PostAsJsonAsync("/api/account/totp/confirm", new { code = codes[0] });
        Assert.Equal(HttpStatusCode.TooManyRequests, confirmed.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var totp = await verifyDb.UserTotps.AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(UserTotp.RecoveryCodeCount, TotpService.CountRecoveryCodes(totp.RecoveryCodeHashes));
    }

    [Fact]
    public async Task ConcurrentApiAndForm_CannotSpendSameRecoveryCodeTwice()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        var (codes, hashes) = TotpService.GenerateRecoveryCodes();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserTotps.Add(new UserTotp
            {
                UserId = user.Id,
                Secret = TotpService.GenerateSecret(),
                ConfirmedAt = DateTime.UtcNow,
                RecoveryCodeHashes = hashes,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        var html = await client.GetStringAsync(RoutePaths.MfaChallenge);
        using var form = Form(html, codes[0]);
        var apiTask = client.PostAsJsonAsync("/api/account/totp/verify", new { code = codes[0] });
        var formTask = client.PostAsync(RoutePaths.MfaChallenge, form);

        await Task.WhenAll(apiTask, formTask);
        using var api = await apiTask;
        using var formResponse = await formTask;
        var apiSucceeded = api.StatusCode == HttpStatusCode.NoContent;
        var formSucceeded = formResponse.StatusCode == HttpStatusCode.Redirect;
        Assert.NotEqual(apiSucceeded, formSucceeded);
        Assert.Equal(apiSucceeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
            apiSucceeded ? formResponse.StatusCode : api.StatusCode);
    }
}
