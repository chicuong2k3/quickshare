using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using FastEndpoints.Swagger;
using System.Security.Claims;
using QuickShare.Server.BackgroundJobs;
using QuickShare.Server.Features.Deliveries;
using QuickShare.Server.Infrastructure.Quota;
using QuickShare.Server.Infrastructure.Storage;
using QuickShare.Server.Data;
using QuickShare.Server.Middleware;
using QuickShare.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");

// Shared DataProtection keyring with Web (both use same ApplicationName + KeyPath).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "QuickShare", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("QuickShare");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString)
        .AddInterceptors(new AuditableEntityInterceptor()));

builder.Services.AddScoped<ISessionTicketDbContext>(sp =>
    sp.GetRequiredService<AppDbContext>());

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

builder.Services.AddScoped<DeliveryService>();

var allowedOrigins = (quickShareOptions.AllowedCorsOrigins ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (allowedOrigins.Length > 0)
            p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        else
            p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "quickshare");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
}, ExtensionDiscovery.ManualOnly);

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();
builder.Services.AddHttpContextAccessor();
builder.Services.AddApiCookieAuth();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// P2P WebRTC signaling
builder.Services.AddSingleton<P2PConnectionManager>();

builder.Services.AddScoped<QuotaEnforcer>();

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
app.UseCors();


if (app.Environment.IsDevelopment())
{
    // Dev-mode: log full request/response for debugging.
    app.UseMiddleware<RequestLoggingMiddleware>();

    // Dev-mode: auto sign-in a local user so you can test APIs without Google OAuth.
    app.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
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

app.UseMiddleware<RateLimitingMiddleware>();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<P2PSignalingMiddleware>();

app.UseFastEndpoints()
   .UseSwaggerGen();
app.MapGet("/", () => Results.Redirect("/swagger"));
await app.RunAsync();
