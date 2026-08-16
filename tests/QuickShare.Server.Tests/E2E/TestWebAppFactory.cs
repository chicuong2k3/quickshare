using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestUserProviderKey = "test-user-key";
    public const string TestUserEmail = "test@quickshare.test";
    public const string TestUserName = "Test User";

    public Guid TestUserId { get; private set; }

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"quickshare-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("UseInMemoryDatabase", "true");
        // FusionCache backs process-wide statics (StaticCache, PlanTierLimits._cache);
        // parallel test factories would leak cached users/pages into each other.
        builder.UseSetting("FusionCache:Enabled", "false");
        builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
        builder.UseSetting("Authentication:GitHub:ClientId", "test-client-id");
        builder.UseSetting("Authentication:GitHub:ClientSecret", "test-client-secret");
        builder.UseSetting("PayOS:ClientId", "test-payos-client");
        builder.UseSetting("PayOS:ApiKey", "test-payos-api-key");
        builder.UseSetting("PayOS:ChecksumKey", "test-payos-checksum-key");
        // The default seeded test user ("test@quickshare.test") is an admin, so the
        // feature-flag admin surface is exercisable from E2E tests.
        builder.UseSetting("Admin:Emails:0", TestUserEmail);

        builder.ConfigureTestServices(services =>
        {
            // Re-register the DbContext with a per-factory SQLite file so every
            // factory (test class) gets an isolated store. SQLite (unlike the
            // InMemory provider) is relational, so ExecuteUpdate/ExecuteDelete and
            // FK constraints behave like production. RemoveAll of the options
            // configuration is required — EF Core 10 registers both
            // DbContextOptions<T> and IDbContextOptionsConfiguration<T>, and leaving
            // the latter would keep the Program.cs registration alive.
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();
            // MigrationsAssembly points at this (migration-less) test assembly so the
            // Program.cs startup block sees no pending migrations (the checked-in
            // migrations are Npgsql SQL); the schema comes from EnsureCreated instead.
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={_dbPath}",
                    sqlite => sqlite.MigrationsAssembly("QuickShare.Server.Tests")));

            // Header-based test auth: authenticates only when X-Test-UserId is
            // present; challenges stay on the Cookie scheme so anonymous flows and
            // /api 401 behavior are unchanged.
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthDefaults.Scheme;
                    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(TestAuthDefaults.Scheme, _ => { });

            services.AddSingleton<IStorageProvider, LocalDiskStorageProviderStub>();
            services.AddScoped<PayOSService, StubPayOSService>();
            RemoveHostedService<PayOSWebhookRegistrationHostedService>(services);
        });
    }

    /// <summary>Client authenticated as the default seeded test user.</summary>
    public HttpClient CreateAuthenticatedClient() => CreateAuthenticatedClient(TestUserProviderKey);

    /// <summary>
    /// Client authenticated as the given provider key (via X-Test-UserId header).
    /// The user must exist — the default is seeded, others via <see cref="SeedUserAsync"/>.
    /// The email claim defaults to the seeded user's email (admin in the test config).
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestUserEmail);
        return client;
    }

    /// <summary>
    /// Creates a user (and its login row) directly in the InMemory DB so the
    /// auth pipeline and GetUserAsync can resolve it.
    /// </summary>
    public async Task<User> SeedUserAsync(string userId, string plan = PlanTierLimits.Free)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users.FindAsync(Guid.Parse(userId));
        if (existing is not null)
        {
            return existing;
        }

        var user = new User
        {
            Id = Guid.Parse(userId),
            Name = TestUserName,
            Email = TestUserEmail,
            Plan = plan
        };
        db.Users.Add(user);

        db.UserLogins.Add(new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = TestAuthProvider.Value,
            ProviderKey = userId
        });

        await db.SaveChangesAsync();
        return user;
    }

    public async ValueTask InitializeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var profile = await UserRegistrationService.RegisterAsync(
                TestAuthProvider.Value, TestUserProviderKey, TestUserName, TestUserEmail,
                null, db, CancellationToken.None);
            TestUserId = profile.Id;
        }

        // Warm-up: first request pays JIT/Razor-compile cost so timing asserts measure steady state.
        using var client = CreateClient();
        await client.GetAsync("/pricing");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException) { }
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (descriptor is not null)
            services.Remove(descriptor);
    }
}

/// <summary>Replaces outbound PayOS API calls with an instant fake checkout link.</summary>
internal sealed class StubPayOSService : PayOSService
{
    public StubPayOSService(Microsoft.Extensions.Configuration.IConfiguration config, Microsoft.Extensions.Logging.ILogger<PayOSService> logger)
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

    /// <summary>Order codes the stubbed PayOS API reports as paid.</summary>
    public static readonly HashSet<long> PaidOrderCodes = [];

    public override Task<bool> IsPaidAsync(long orderCode, CancellationToken ct)
    {
        lock (PaidOrderCodes)
        {
            return Task.FromResult(PaidOrderCodes.Contains(orderCode));
        }
    }
}

internal sealed class LocalDiskStorageProviderStub : IStorageProvider
{
    /// <summary>Size reported by GetObjectSizeAsync; -1 (default) means "unknown", which skips the commit-time size check.</summary>
    public long NextObjectSize { get; set; } = -1;

    public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
        => Task.FromResult(NextObjectSize);

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

    public Task UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri("http://stub.local/download"));

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CopyObjectAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2MultipartInfo>>([]);
}
