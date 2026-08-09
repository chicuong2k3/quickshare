using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public static class TestAuthDefaults
{
    public const string Scheme = "TestAuth";
    public const string UserIdHeader = "X-Test-UserId";
}

public sealed class TestAuthSchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Test-only authentication scheme. When the <c>X-Test-UserId</c> header is
/// present, the request is authenticated as that user (provider = "test").
/// Without the header the request stays anonymous — which keeps all existing
/// public/anonymous flows and cookie-based challenges intact.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<TestAuthSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<TestAuthSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers[TestAuthDefaults.UserIdHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(AuthConstants.ProviderClaimType, TestAuthProvider.Value),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@localhost")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class TestAuthProvider
{
    public const string Value = "test";
}
