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
        // Storage regions (issue #119). Configured here so residency is exercised end to end:
        // without a region table the resolver returns null for everyone and the whole feature
        // silently no-ops in tests, which is exactly how its commit-path stamp went missing.
        builder.UseSetting("ObjectStore:Regions:0:Code", "eu");
        builder.UseSetting("ObjectStore:Regions:0:Label", "European Union");
        builder.UseSetting("ObjectStore:Regions:0:BucketName", "quickshare-eu");
        builder.UseSetting("ObjectStore:Regions:1:Code", "us");
        builder.UseSetting("ObjectStore:Regions:1:Label", "United States");
        builder.UseSetting("ObjectStore:Regions:1:BucketName", "quickshare-us");
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
            services.RemoveAll<IRemoteUrlFetcher>();
            services.AddSingleton<IRemoteUrlFetcher, StubRemoteUrlFetcher>();
            services.RemoveAll<QuickShare.Infrastructure.Push.IPushService>();
            services.AddSingleton<QuickShare.Infrastructure.Push.IPushService, StubPushService>();
            services.AddScoped<PayOSService, StubPayOSService>();
            // White-label domain verification (issue #81) asks DNS a question; a suite that
            // asked the real resolver would be testing the network.
            services.RemoveAll<QuickShare.Features.Workspaces.Domains.DomainVerifier>();
            services.AddScoped<QuickShare.Features.Workspaces.Domains.DomainVerifier, StubDomainVerifier>();
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
        var client = CreateUnverifiedClient(userId);
        // Second-factor-verified by default (issue #128): the gate only challenges accounts that
        // enrolled or are out of policy grace, and a suite that silently ran every request as
        // un-verified would be testing the gate rather than the feature under test.
        client.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");
        return client;
    }

    /// <summary>A session that has not answered the second-factor challenge (issue #128).</summary>
    public HttpClient CreateUnverifiedClient(string userId)
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

/// <summary>
/// Answers domain verification from a set instead of the resolver (issue #81). A host is
/// "pointed at us" once a test says so, which is what publishing a CNAME means here.
/// </summary>
internal sealed class StubDomainVerifier(
    Microsoft.Extensions.Options.IOptions<QuickShare.Infrastructure.Email.EmailOptions> emailOptions,
    Microsoft.Extensions.Logging.ILogger<QuickShare.Features.Workspaces.Domains.DomainVerifier> logger)
    : QuickShare.Features.Workspaces.Domains.DomainVerifier(emailOptions, logger)
{
    /// <summary>Hosts whose CNAME the test has published.</summary>
    public static readonly HashSet<string> Pointed = new(StringComparer.OrdinalIgnoreCase);

    public override string CanonicalHost => "app.quickshare.test";

    public override Task<string?> CheckAsync(string host, CancellationToken ct)
    {
        lock (Pointed)
        {
            return Task.FromResult(Pointed.Contains(host)
                ? null
                : $"{host} does not point at {CanonicalHost} yet.");
        }
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

internal sealed class StubRemoteUrlFetcher : IRemoteUrlFetcher
{
    public Task<RemoteUrlResult> FetchAsync(string url, long maxBytes, CancellationToken ct)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RemoteUrlResult(RemoteUrlStatus.InvalidUrl));
        }

        return Task.FromResult(new RemoteUrlResult(
            RemoteUrlStatus.Ok,
            new MemoryStream(new byte[] { 1, 2, 3, 4 }),
            "application/octet-stream",
            "remote.bin"));
    }
}

internal sealed class StubPushService : QuickShare.Infrastructure.Push.IPushService
{
    public string PublicKey => "test-vapid-public-key";
    public bool IsConfigured => true;

    public Task SendAsync(
        IReadOnlyList<QuickShare.Data.Entities.PushSubscription> subscriptions,
        string title, string message, string link, CancellationToken ct)
        => Task.CompletedTask;
}

internal sealed class LocalDiskStorageProviderStub : IStorageProvider
{
    /// <summary>Size reported by GetObjectSizeAsync; -1 (default) means "unknown", which skips the commit-time size check.</summary>
    public long NextObjectSize { get; set; } = -1;
    public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
    {
        // An explicit NextObjectSize still wins, so the tests that drive the commit-time size
        // check keep driving it; a stored object answers for itself.
        if (NextObjectSize >= 0)
            return Task.FromResult(NextObjectSize);

        lock (_objects)
        {
            return Task.FromResult(_objects.TryGetValue(objectKey, out var bytes) ? bytes.LongLength : -1L);
        }
    }

    /// <summary>
    /// Builds the *real* key shape rather than a stub-specific one. The old "stub/{id}/{name}"
    /// key meant nothing downstream could read a region out of it, which is precisely why the
    /// missing commit-time region stamp (issue #119) survived a green suite.
    /// </summary>
    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, string? region = null, CancellationToken ct = default)
        => Task.FromResult(new R2InitResult("stub-upload-id", R2ObjectKey.For(Guid.Empty, deliveryId, fileName, region)));

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2PartUrl>>(partNumbers
            .Select(n => new R2PartUrl(n, new Uri("http://stub.local/")))
            .ToList());

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => Task.FromResult(new CompletionResult(true, null));

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Objects this stub actually holds. It used to throw the bytes away, which made every
    /// stream-in/stream-out path (the protocol mounts, #195/#265) untestable while looking
    /// green — the same class of defect the batch-11 audit found in the object *key*.
    /// </summary>
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public async Task UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        lock (_objects)
        {
            _objects[objectKey] = buffer.ToArray();
        }
    }

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri("http://stub.local/download"));

    /// <summary>Prefixes DeleteObjectsAsync was asked to remove — lets a sweep test assert what survived.</summary>
    public List<string> DeletedPrefixes { get; } = [];

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
    {
        DeletedPrefixes.Add(prefix);
        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        lock (_objects)
        {
            _objects.Remove(objectKey);
        }

        return Task.CompletedTask;
    }

    public Task CopyObjectAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Bytes returned by OpenReadAsync; used by the ZIP preview test.</summary>
    public byte[] NextReadBytes { get; set; } = [];

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        lock (_objects)
        {
            if (_objects.TryGetValue(objectKey, out var stored))
                return Task.FromResult<Stream?>(new MemoryStream(stored));
        }

        return Task.FromResult<Stream?>(new MemoryStream(NextReadBytes));
    }

    public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2MultipartInfo>>([]);
}
