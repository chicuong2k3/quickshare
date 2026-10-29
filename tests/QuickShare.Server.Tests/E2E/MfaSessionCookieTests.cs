using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickShare.Features.Auth.Totp;
using QuickShare.Features.Users.Sessions;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class MfaSessionCookieTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public MfaSessionCookieTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task VerifiedCookie_PreservesExistingSessionAndAddsMfaClaim()
    {
        var sessionId = Guid.CreateVersion7().ToString();
        using var scope = _factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
            new Claim(SessionService.SessionClaimType, sessionId),
        ], CookieAuthenticationDefaults.AuthenticationScheme));

        await MfaSession.MarkVerifiedAsync(context);

        var cookie = context.Response.Headers.SetCookie.Single(value => value!.StartsWith(AuthConstants.CookieName + "=", StringComparison.Ordinal));
        var encoded = cookie!.Split(';')[0].Split('=', 2)[1];
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(encoded));
        Assert.NotNull(ticket);
        Assert.True(MfaSession.IsVerified(ticket.Principal));
        Assert.Equal(sessionId, ticket.Principal.FindFirst(SessionService.SessionClaimType)?.Value);
    }
}
