using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using FastEndpoints.Swagger;
using System.Security.Claims;
using QuickShare;
using QuickShare.Features.Deliveries;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Quota;
using QuickShare.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");

// Shared DataProtection keyring (same ApplicationName + KeyPath the two projects used before).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "QuickShare", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("QuickShare");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString)
        .AddInterceptors(new AuditableEntityInterceptor()));

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

builder.Services.AddHostedService<DeliveryCleanupService>();
builder.Services.AddHostedService<ZombiePartSweeper>();
builder.Services.AddHostedService<DeliveryExpiryJob>();
builder.Services.AddHostedService<SubscriptionExpiryJob>();

builder.Services.AddScoped<DeliveryService>();


builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "quickshare");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
}, ExtensionDiscovery.ManualOnly);

// ── API (FastEndpoints + Swagger) ──
builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();
builder.Services.AddHttpContextAccessor();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// P2P WebRTC signaling
builder.Services.AddSingleton<P2PConnectionManager>();

builder.Services.AddScoped<QuotaEnforcer>();

// ── Web (Blazor Static SSR + htmx) ──
// In .NET 10 Static Server rendering is the default for AddRazorComponents().
builder.Services.AddRazorComponents()
    .AddHtmx();
builder.Services.AddAntiforgery();
builder.Services.AddLocalization();

// ── Unified cookie auth + Google OAuth ──
// A single AddAuthentication call serves both the API (401/403 on /api/*) and the
// SSR pages (redirect to /login for browser navigations).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AuthConstants.CookieName;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            // API callers get a status code; browser page requests get the redirect.
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.GetPendingMigrations().Any())
        await db.Database.MigrateAsync();

    if (!await db.Folders.AnyAsync(f => f.Name == "/"))
    {
        db.Folders.Add(new Folder { Id = Guid.CreateVersion7(), Name = "/" });
        await db.SaveChangesAsync();
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

app.UseExceptionHandler();


if (app.Environment.IsDevelopment())
{
    // Dev-mode: log full request/response for debugging.
    app.UseMiddleware<RequestLoggingMiddleware>();

    // Dev-mode: auto sign-in a local user so you can test without Google OAuth.
    // Runs before UseAuthentication/MapRazorComponents so [Authorize] pages see the dev user.
    app.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true && ctx.Request.Query["logout"] != "1")
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "dev-user"),
                new Claim(AuthConstants.ProviderClaimType, AuthConstants.DevProvider),
                new Claim(ClaimTypes.Name, "Dev User"),
                new Claim(ClaimTypes.Email, "dev@localhost")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            ctx.User = principal;
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }
        await next();
    });
}

// Serve wwwroot/ (and optimized static assets if a manifest is present).
app.UseStaticFiles();

app.UseMiddleware<RateLimitingMiddleware>();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();
app.UseHtmxAntiforgery();

app.UseMiddleware<P2PSignalingMiddleware>();

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
app.MapGet("/logout", async (HttpContext context) =>
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
