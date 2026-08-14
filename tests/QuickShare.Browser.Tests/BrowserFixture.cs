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
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

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
        builder.UseSetting("UseInMemoryDatabase", "true");
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

            // Stub R2 so upload flows work end-to-end in the browser; part PUTs go
            // to http://stub.local/... which tests intercept with Playwright routing.
            services.AddSingleton<IStorageProvider, BrowserStorageStub>();
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

/// <summary>R2 stub for browser tests: part URLs point at http://stub.local so
/// Playwright route interception can fulfill (or abort, to simulate network loss)
/// the part PUTs; commit and download URL generation always succeed.</summary>
internal sealed class BrowserStorageStub : IStorageProvider
{
    public const string PartUrlPrefix = "http://stub.local/part/";

    public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
        => Task.FromResult(-1L);

    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
        => Task.FromResult(new R2InitResult("stub-upload-id", $"stub/{deliveryId}/{fileName}"));

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2PartUrl>>(partNumbers
            .Select(n => new R2PartUrl(n, new Uri($"{PartUrlPrefix}{n}")))
            .ToList());

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => Task.FromResult(new CompletionResult(true, null));

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri("http://stub.local/download"));

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2MultipartInfo>>([]);
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
