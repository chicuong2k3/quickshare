using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Force an isolated InMemory database. Program.cs only picks InMemory
            // when Type.GetType("Microsoft.EntityFrameworkCore.InMemoryDatabaseProvider, ...")
            // succeeds at host-build time, which is unreliable inside
            // WebApplicationFactory — so tests would otherwise silently hit the
            // real Postgres (shared state bleeds across runs). A unique name per
            // factory instance isolates every test class. RemoveAll is required so
            // the Npgsql options from Program.cs are not left registered too
            // (EF forbids two providers in one service provider).
            // EF Core 10 AddDbContext registers both DbContextOptions<T>
            // and IDbContextOptionsConfiguration<T>. The latter is what the
            // Npgsql registration from Program.cs survives in — RemoveAll of
            // just the options leaves both providers registered and EF throws
            // "Only a single database provider can be registered".
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase($"QuickShareTests-{Guid.NewGuid():N}"));

            // Register the header-based test authentication scheme. It only
            // authenticates when X-Test-UserId is present. Authenticate scheme
            // becomes TestAuth so requests can be impersonated; challenges stay
            // on the Cookie scheme so [Authorize] pages still redirect to /login
            // and existing anonymous tests keep passing.
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthDefaults.Scheme;
                    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(TestAuthDefaults.Scheme, _ => { });

            services.AddSingleton<IStorageProvider, LocalDiskStorageProviderStub>();
            RemoveHostedService<DeliveryCleanupService>(services);
            RemoveHostedService<ZombiePartSweeper>(services);
            RemoveHostedService<DeliveryExpiryJob>(services);
            RemoveHostedService<SubscriptionExpiryJob>(services);
        });
    }

    /// <summary>
    /// Returns an anonymous client (existing tests rely on cookie redirects).
    /// </summary>
    public HttpClient CreateAuthenticatedClient() => CreateClient();

    /// <summary>
    /// Creates a client authenticated as the given user id (via X-Test-UserId header).
    /// The user must be seeded with <see cref="SeedUserAsync"/> first.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, userId);
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
            Name = "Test User",
            Email = "test@localhost",
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
        using var scope = Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

internal sealed class LocalDiskStorageProviderStub : IStorageProvider
{
    public Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default)
        => Task.FromResult($"stub://{chunkId}");

    public Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());

    public Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default)
        => Task.CompletedTask;

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
