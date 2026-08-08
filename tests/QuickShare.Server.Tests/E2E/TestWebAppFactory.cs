using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickShare.Data;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestUserProviderKey = "test-user-key";
    public const string TestUserEmail = "test@quickshare.test";
    public const string TestUserName = "Test User";

    public Guid TestUserId { get; private set; }

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
            services.AddSingleton<IStorageProvider, LocalDiskStorageProviderStub>();
            services.AddScoped<PayOSService, StubPayOSService>();
            RemoveHostedService<DeliveryCleanupService>(services);
            RemoveHostedService<ZombiePartSweeper>(services);
            RemoveHostedService<DeliveryExpiryJob>(services);
            RemoveHostedService<SubscriptionExpiryJob>(services);
            RemoveHostedService<PayOSWebhookRegistrationHostedService>(services);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "1");
        return client;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await UserRegistrationService.RegisterAsync(
            AuthConstants.GoogleProvider, TestUserProviderKey, TestUserName, TestUserEmail,
            null, db, CancellationToken.None);

        // Parallel test classes share the in-memory store and may double-register the
        // test user; resolve the id with the same lookup the app's auth uses so it
        // always matches the user requests run as.
        var login = await db.UserLogins
            .FirstAsync(l => l.Provider == AuthConstants.GoogleProvider && l.ProviderKey == TestUserProviderKey);
        TestUserId = login.UserId;

        // Warm-up: first request pays JIT/Razor-compile cost so timing asserts measure steady state.
        using var client = CreateClient();
        await client.GetAsync("/pricing");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (descriptor is not null)
            services.Remove(descriptor);
    }
}

/// <summary>
/// Authenticates any request carrying the X-Test-Auth header as the seeded test user.
/// Requests without the header stay anonymous, so both paths are testable.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string AuthHeader = "X-Test-Auth";

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
            new Claim(ClaimTypes.NameIdentifier, TestWebAppFactory.TestUserProviderKey),
            new Claim(ClaimTypes.Name, TestWebAppFactory.TestUserName),
            new Claim(ClaimTypes.Email, TestWebAppFactory.TestUserEmail),
            new Claim(AuthConstants.ProviderClaimType, AuthConstants.GoogleProvider),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>Replaces outbound PayOS API calls with an instant fake checkout link.</summary>
internal sealed class StubPayOSService : PayOSService
{
    public StubPayOSService(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<PayOSService> logger)
        : base(config, logger)
    {
    }

    public override Task<PayOSPaymentResult> CreatePaymentLinkAsync(long orderCode, int amountVnd, string description, CancellationToken ct)
        => Task.FromResult(new PayOSPaymentResult
        {
            Success = true,
            CheckoutUrl = $"https://pay.stub.local/{orderCode}",
            QrCode = "stub-qr",
            PaymentLinkId = $"stub-link-{orderCode}",
            Bin = "970422",
            AccountNumber = "0000000000",
            AccountName = "STUB ACCOUNT"
        });
}

internal sealed class LocalDiskStorageProviderStub : IStorageProvider
{
    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
        => Task.FromResult(new R2InitResult("stub-upload-id", $"stub/{deliveryId}/{fileName}"));

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2PartUrl>>(partNumbers
            .Select(n => new R2PartUrl(n, new Uri("http://stub.local/")))
            .ToList());

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => Task.FromResult(new CompletionResult(true, null));

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri("http://stub.local/download"));

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => Task.CompletedTask;
}
