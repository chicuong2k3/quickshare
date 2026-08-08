using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using QuickShare.Data;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Boots the app on a real Kestrel port (so a real browser can hit it)
/// and launches a headless Chromium via Playwright.
/// </summary>
public sealed class BrowserFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = null!;

    private IPlaywright _playwright = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
        builder.UseSetting("PayOS:ClientId", "test-payos-client");
        builder.UseSetting("PayOS:ApiKey", "test-payos-api-key");
        builder.UseSetting("PayOS:ChecksumKey", "test-payos-checksum-key");

        builder.ConfigureTestServices(services =>
        {
            RemoveHostedService<DeliveryCleanupService>(services);
            RemoveHostedService<ZombiePartSweeper>(services);
            RemoveHostedService<DeliveryExpiryJob>(services);
            RemoveHostedService<SubscriptionExpiryJob>(services);
            RemoveHostedService<PayOSWebhookRegistrationHostedService>(services);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public async ValueTask InitializeAsync()
    {
        UseKestrel(0);
        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses!.Addresses.First().Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await UserRegistrationService.RegisterAsync(
                AuthConstants.GoogleProvider, TestAuthHandler.ProviderKey, "Browser Test User",
                "browser@quickshare.test", null, db, CancellationToken.None);
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>New browser context; pass authenticated=true to run as the seeded test user.</summary>
    public Task<IBrowserContext> CreateContextAsync(bool authenticated = false)
    {
        var options = new BrowserNewContextOptions { IgnoreHTTPSErrors = true };
        if (authenticated)
            options.ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" };
        return Browser.NewContextAsync(options);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
        await base.DisposeAsync();
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (descriptor is not null)
            services.Remove(descriptor);
    }
}

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string AuthHeader = "X-Test-Auth";
    public const string ProviderKey = "browser-test-user-key";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(AuthHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, ProviderKey),
            new Claim(ClaimTypes.Name, "Browser Test User"),
            new Claim(ClaimTypes.Email, "browser@quickshare.test"),
            new Claim(AuthConstants.ProviderClaimType, AuthConstants.GoogleProvider),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
