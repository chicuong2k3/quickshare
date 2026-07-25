using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using FastEndpoints.Swagger;
using System.Security.Claims;
using QuickShare.Server.BackgroundJobs;
using QuickShare.Server.Data;
using QuickShare.Server.Features.Deliveries;
using QuickShare.Server.Infrastructure.Quota;
using QuickShare.Server.Infrastructure.Storage;
using QuickShare.Server.Middleware;

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
    opts.UseNpgsql(connectionString));

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.Section));

builder.Services.Configure<R2Options>(
    builder.Configuration.GetSection(R2Options.Section));

builder.Services.AddSingleton<R2StorageProvider>(sp =>
{
    var options = sp.GetRequiredService<IOptions<R2Options>>();
    var logger = sp.GetRequiredService<ILogger<R2StorageProvider>>();
    return new R2StorageProvider(options, logger);
});

// IStorageProvider resolves to R2 by default.
// Consumers that specifically need local-disk should inject LocalDiskStorageProvider directly.
builder.Services.AddSingleton<IStorageProvider>(sp =>
    sp.GetRequiredService<R2StorageProvider>());

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

// Google OAuth — chained on top of cookie auth.
var googleSection = builder.Configuration.GetSection("Authentication:Google");
var googleClientId = googleSection["ClientId"] ?? "";
var googleClientSecret = googleSection["ClientSecret"] ?? "";
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.CallbackPath = "/auth/google/callback";
            options.Events.OnCreatingTicket = async ctx =>
            {
                var email = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value;
                var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;
                var subject = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(subject))
                    return;

                await using var scope = ctx.HttpContext.RequestServices.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var login = await db.UserLogins
                    .Include(l => l.User)
                    .FirstOrDefaultAsync(l => l.Provider == "google" && l.ProviderKey == subject);

                User user;
                if (login?.User is not null)
                {
                    user = login.User;
                    if (user.Name != name && !string.IsNullOrEmpty(name))
                    {
                        user.Name = name;
                        user.UpdatedAt = DateTime.UtcNow;
                    }
                    if (user.Email != email && !string.IsNullOrEmpty(email))
                    {
                        user.Email = email;
                        user.UpdatedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    user = new User
                    {
                        Id = Guid.CreateVersion7(),
                        Name = name ?? email ?? "unknown",
                        Email = email ?? "",
                        AvatarUrl = "",
                        Plan = PlanTierLimits.Free
                    };
                    db.Users.Add(user);

                    db.UserLogins.Add(new UserLogin
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = user.Id,
                        Provider = "google",
                        ProviderKey = subject
                    });

                    // Seed initial usage record for the current month.
                    var now = DateTime.UtcNow;
                    var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    db.UserUsages.Add(new UserUsage
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = user.Id,
                        PeriodStart = periodStart,
                        TotalBytesUploaded = 0,
                        TransferCount = 0
                    });
                }

                await db.SaveChangesAsync();

                var identity = ctx.Principal!.Identities.First();
                identity.AddClaim(new Claim("local_user_id", user.Id.ToString()));
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            };
        });
}

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

    var r2 = scope.ServiceProvider.GetRequiredService<R2StorageProvider>();
    await r2.EnsureBucketExistsAsync();
}

app.UseExceptionHandler();
app.UseCors();
app.UseStaticFiles();

// Dev-mode: log full request/response for debugging.
if (app.Environment.IsDevelopment())
    app.UseMiddleware<RequestLoggingMiddleware>();

// Dev-mode: auto sign-in a local user so you can test APIs without Google OAuth.
if (app.Environment.IsDevelopment())
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "dev-user"),
                new Claim("provider", "dev"),
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
