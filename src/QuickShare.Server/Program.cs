using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using FastEndpoints.Swagger;
using System.Security.Claims;

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

builder.Services.AddSingleton<IStorageProvider>(sp =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>();
    var logger = sp.GetRequiredService<ILogger<LocalDiskStorageProvider>>();
    return new LocalDiskStorageProvider(options, logger);
});

var quickShareConfig = builder.Configuration.GetSection(QuickShareOptions.Section);
builder.Services.Configure<QuickShareOptions>(quickShareConfig);
var quickShareOptions = quickShareConfig.Get<QuickShareOptions>() ?? new QuickShareOptions();

builder.Services.AddHostedService<DeliveryCleanupService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints()
   .UseSwaggerGen();
app.MapGet("/", () => Results.Redirect("/swagger"));
await app.RunAsync();
