using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using QuickShare;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Shares.P2P;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.FeatureFlags;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Middleware;
using QuickShare.Infrastructure.Telemetry;
using Serilog;
using Serilog.Enrichers.Span;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Claims;
using Wolverine.ErrorHandling;
using ZiggyCreatures.Caching.Fusion;


// ── Load .env file (if present) for local development ──
// Docker compose injects these as env vars; local `dotnet run` needs them loaded manually.
// Walk up from the current directory to find .env (repo root may differ from project dir).
static string? FindEnvFile()
{
    var dir = Directory.GetCurrentDirectory();
    for (var i = 0; i < 6; i++)
    {
        var candidate = Path.Combine(dir, ".env");
        if (File.Exists(candidate))
            return candidate;
        var parent = Directory.GetParent(dir);
        if (parent is null)
            break;
        dir = parent.FullName;
    }
    return null;
}


// Convert UPPER_SNAKE_CASE to PascalCase (e.g. "ACCESS_KEY_ID" → "AccessKeyId")
static string ToPascalCase(string snake)
{
    var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
    return string.Concat(parts.Select(p =>
        char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
}

var envFilePath = FindEnvFile();
if (envFilePath is not null)
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            continue;
        var eqIndex = trimmed.IndexOf('=');
        if (eqIndex <= 0)
            continue;
        var key = trimmed[..eqIndex].Trim();
        var value = trimmed[(eqIndex + 1)..].Trim();
        if (string.IsNullOrEmpty(value))
            continue;

        Environment.SetEnvironmentVariable(key, value);

        // Also set .NET-style __ variant so config binding works.
        // .env uses R2_ACCESS_KEY_ID; .NET expects R2__AccessKeyId → R2:AccessKeyId.
        var firstUnderscore = key.IndexOf('_');
        if (firstUnderscore > 0)
        {
            var section = key[..firstUnderscore];
            var propertySnake = key[(firstUnderscore + 1)..];
            var dotnetKey = $"{section}__{ToPascalCase(propertySnake)}";
            Environment.SetEnvironmentVariable(dotnetKey, value);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.WithSpan();
});

const string DefaultConnectionName = "DefaultConnection";
const string MissingConnectionStringError = "ConnectionStrings:DefaultConnection missing";
const int DatabaseKeepAliveSeconds = 30;
const int DatabaseRetryAttemptCount = 5;
var databaseMaxRetryDelay = TimeSpan.FromSeconds(10);
var databaseRetrySchedule = new[]
{
    TimeSpan.FromSeconds(2),
    TimeSpan.FromSeconds(5),
    TimeSpan.FromSeconds(15)
};

var configuredConnectionString = builder.Configuration.GetConnectionString(DefaultConnectionName)
    ?? throw new InvalidOperationException(MissingConnectionStringError);
var connectionBuilder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
{
    KeepAlive = DatabaseKeepAliveSeconds,
    TcpKeepAlive = true,
    TcpKeepAliveTime = 30,
    TcpKeepAliveInterval = 10,
};
var connectionString = connectionBuilder.ConnectionString;
// Shared DataProtection keyring (same ApplicationName + KeyPath the two projects used before).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "QuickShare", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("QuickShare");

// Set by the test factories; keeps the app on Npgsql everywhere else (incl. dotnet-ef design time).
var useInMemoryDb = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

if (useInMemoryDb)
{
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseInMemoryDatabase("QuickShareTests"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.EnableRetryOnFailure(DatabaseRetryAttemptCount, databaseMaxRetryDelay, null))
            .AddInterceptors(new AuditableEntityInterceptor()));
}

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.Section));

var r2Section = builder.Configuration.GetSection(R2Options.Section);
builder.Services.Configure<R2Options>(r2Section);

var r2Options = r2Section.Get<R2Options>() ?? new();
var hasR2 = !string.IsNullOrEmpty(r2Options.AccountId) &&
            !string.IsNullOrEmpty(r2Options.AccessKeyId) &&
            !string.IsNullOrEmpty(r2Options.AccessKeySecret);

if (hasR2)
{
    builder.Services.AddSingleton<R2StorageProvider>();
    // IStorageProvider resolves to R2 by default.
    // Consumers that specifically need local-disk should inject LocalDiskStorageProvider directly.
    builder.Services.AddSingleton<IStorageProvider>(sp =>
        sp.GetRequiredService<R2StorageProvider>());
}
else
{
    // R2 not configured — fall back to local disk so the app can still start.
    // Upload/download features that require R2 will throw NotSupportedException.
    builder.Services.AddSingleton<IStorageProvider, LocalDiskStorageProvider>();
}

var quickShareConfig = builder.Configuration.GetSection(QuickShareOptions.Section);
builder.Services.Configure<QuickShareOptions>(quickShareConfig);
var quickShareOptions = quickShareConfig.Get<QuickShareOptions>() ?? new QuickShareOptions();

// ── Email (Brevo SMTP by default; any SMTP relay works via config) ──
builder.Services.Configure<QuickShare.Infrastructure.Email.EmailOptions>(
    builder.Configuration.GetSection(QuickShare.Infrastructure.Email.EmailOptions.Section));
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.EmailSender>();
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.UnsubscribeTokens>();
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.SenderEmailTokens>();
builder.Services.AddSingleton<QuickShare.Infrastructure.Email.SenderEmailRateLimiter>();

builder.Services.AddHostedService<DeliveryCleanupService>();
builder.Services.AddHostedService<ZombiePartSweeper>();
builder.Services.AddHostedService<DeliveryExpiryJob>();
builder.Services.AddHostedService<SubscriptionExpiryJob>();
builder.Services.AddHostedService<DownloadNotificationJob>();
builder.Services.AddHostedService<PayOSWebhookRegistrationHostedService>();
if (hasR2)
{
    // Orphan sweep lists/deletes R2 objects — meaningless (and throwing) on local disk.
    builder.Services.AddHostedService<OrphanSweepJob>();
}

builder.Services.AddScoped<PayOSService>();
builder.Services.AddScoped<QuickShare.Features.Payments.CheckoutService>();

// ── FusionCache with kill-switch toggle ──
var cacheSection = builder.Configuration.GetSection(CacheSettings.Section);
builder.Services.Configure<CacheSettings>(cacheSection);
var cacheSettings = cacheSection.Get<CacheSettings>() ?? new CacheSettings();

builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(cacheSettings.DefaultDurationMinutes),
        FailSafeMaxDuration = TimeSpan.FromHours(cacheSettings.FailSafeMaxDurationHours),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(cacheSettings.FailSafeThrottleDurationSeconds),
    });

builder.Services.AddSingleton<CacheService>();

// ── Feature flags + admin gate ──
builder.Services.Configure<FeatureFlagSettings>(builder.Configuration.GetSection(FeatureFlagSettings.Section));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
builder.Services.AddScoped<IFeatureFlagService, FeatureFlagService>();

builder.Host.UseWolverine(opts =>
{
    // In-memory DB (tests) runs Wolverine without durable Postgres persistence.
    if (!useInMemoryDb)
    {
        opts.PersistMessagesWithPostgresql(connectionString, "quickshare");

        // Single-instance app: Solo avoids Balanced-mode leader election and the
        // wolverine_nodes agent registry, so no stale-node StopAgent timeouts on health checks.
        opts.Durability.Mode = DurabilityMode.Solo;
        opts.Policies.OnException<NpgsqlException>().ScheduleRetry(databaseRetrySchedule);
        opts.Policies.OnException<IOException>().ScheduleRetry(databaseRetrySchedule);
        opts.Policies.OnException<SocketException>().ScheduleRetry(databaseRetrySchedule);
    }
}, ExtensionDiscovery.ManualOnly);

// ── API (FastEndpoints + Swagger) ──
builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();
builder.Services.AddHttpContextAccessor();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// P2P WebRTC signaling
builder.Services.AddSingleton<P2PConnectionManager>();


// ── Web (Blazor Static SSR + htmx) ──
// In .NET 10 Static Server rendering is the default for AddRazorComponents().
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all proxies — safe behind Cloudflare Tunnel / Docker bridge
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRazorComponents()
    .AddHtmx();
builder.Services.AddLocalization();
builder.Services.AddAntiforgery();

// ── Unified cookie auth + Google OAuth ──
// A single AddAuthentication call serves both the API (401/403 on /api/*) and the
// SSR pages (redirect to /login for browser navigations).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AuthConstants.CookieName;
        options.LoginPath = RoutePaths.Login;
        options.LogoutPath = RoutePaths.Logout;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            // API callers get a status code; browser page requests get the redirect.
            if (ctx.Request.Path.StartsWithSegments(RoutePaths.Api))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments(RoutePaths.Api))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        options.Events.OnCreatingTicket = async ctx =>
        {
            ctx.Identity?.AddClaim(new Claim(AuthConstants.ProviderClaimType, AuthConstants.GoogleProvider));

            var email = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value;
            var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;
            var subject = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var avatar = ctx.User.TryGetProperty("picture", out var pic) ? pic.GetString() : null;

            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(subject))
            {
                // Register in-process (no HTTP roundtrip) via the shared registration service.
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                await UserRegistrationService.RegisterAsync(
                    AuthConstants.GoogleProvider,
                    subject,
                    name ?? email,
                    email,
                    avatar,
                    db,
                    ctx.HttpContext.RequestAborted);
            }
        };
    });

builder.Services.AddAuthorization();

// ── Observability: health checks + Prometheus metrics ──
// ponytail: R2 reachability is verified at startup via EnsureBucketExistsAsync;
// the old AddUrlGroup check did an unauthenticated GET that R2 always rejects,
// so /health was permanently 503. Add an authenticated R2 check with #89 if needed.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, healthQuery: "SELECT 1;", name: "postgresql", tags: ["db"]);

// ── Observability: OpenTelemetry tracing → Tempo (via Grafana Alloy) ──
const string ObservabilityOtlpEndpointKey = "Observability:OtlpEndpoint";
const string DefaultOtlpEndpoint = "http://localhost:4318";
var otlpEndpoint = builder.Configuration[ObservabilityOtlpEndpointKey] ?? DefaultOtlpEndpoint;
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ActivitySources.Name))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        if (db.Database.GetPendingMigrations().Any())
            await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (hasR2)
    {
        var r2 = scope.ServiceProvider.GetRequiredService<R2StorageProvider>();
        await r2.EnsureBucketExistsAsync();
    }
    else
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("R2 not configured — using LocalDiskStorageProvider. Upload/download via R2 will throw.");
    }
}

// Wire static cache holder so static classes (PlanTierLimits) can use cache.
var cacheService = app.Services.GetRequiredService<CacheService>();
StaticCache.Initialize(cacheService);
PlanTierLimits.InitializeCache(cacheService);

app.UseExceptionHandler();

// ── Observability endpoints (before auth — metrics are internal) ──
app.UseHttpMetrics(); // capture HTTP-level metrics (duration, status code, etc.)
app.MapHealthChecks("/health");
app.MapMetrics();



// Must be early — before auth, before static files.
app.UseForwardedHeaders();
if (app.Environment.IsDevelopment())
{
    // Dev-mode: log full request/response for debugging.
    app.UseMiddleware<RequestLoggingMiddleware>();
}

// Serve wwwroot/ (and optimized static assets if a manifest is present).
app.UseStaticFiles();

// ── Localization (cookie-based culture, falls back to Accept-Language header) ──
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("vi") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = { new CookieRequestCultureProvider() }
});

app.UseMiddleware<RateLimitingMiddleware>();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();
app.UseHtmxAntiforgery();

app.UseMiddleware<P2PSignalingMiddleware>();

// ── HTTP caching for HTMX/SSR pages ──
app.UseMiddleware<HtmxCacheMiddleware>();

// ── API endpoints ──
app.UseFastEndpoints()
   .UseSwaggerGen();

// ── Web (Blazor SSR) ──
app.MapStaticAssets();
app.MapRazorComponents<App>();

// OAuth challenge endpoint.
app.MapGet("/login/{provider}", async (string provider, string? returnUrl, HttpContext context, IAuthenticationSchemeProvider schemes) =>
{
    var scheme = (await schemes.GetAllSchemesAsync())
        .FirstOrDefault(s => string.Equals(s.Name, provider, StringComparison.OrdinalIgnoreCase));
    if (scheme is null)
        return Results.NotFound();
    var props = new AuthenticationProperties { RedirectUri = returnUrl ?? "/" };
    await context.ChallengeAsync(scheme.Name, props);
    return Results.Empty;
});

// Logout endpoint.
app.MapGet(RoutePaths.Logout, async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/?logout=1");
});

// Dedicated antiforgery token endpoint (good practice for SSR apps that POST).
app.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
});

await app.RunAsync();

// Disables UseAntiforgery() validation for specific endpoints (e.g. file upload).
internal sealed class SkipAntiforgeryValidation : IAntiforgeryMetadata
{
    public static readonly SkipAntiforgeryValidation Instance = new();
    public bool RequiresValidation => false;
}
