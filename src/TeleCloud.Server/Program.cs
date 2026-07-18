using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using FastEndpoints.Swagger;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");

// Shared DataProtection keyring with Web (both use same ApplicationName + KeyPath).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "TeleCloud", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("TeleCloud");

builder.Services.AddDbContext<TeleCloudDbContext>(opts =>
    opts.UseNpgsql(connectionString));

builder.Services.Configure<TeleCloudOptions>(
    builder.Configuration.GetSection(TeleCloudOptions.Section));

var quickShareConfig = builder.Configuration.GetSection(QuickShareOptions.Section);
builder.Services.Configure<QuickShareOptions>(quickShareConfig);
var quickShareOptions = quickShareConfig.Get<QuickShareOptions>() ?? new QuickShareOptions();

builder.Services.AddSingleton<Client>(sp =>
{
    var options = sp.GetRequiredService<IOptions<TeleCloudOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ApiId) || string.IsNullOrWhiteSpace(options.ApiHash))
        throw new InvalidOperationException(
            "TeleCloud:ApiId and TeleCloud:ApiHash must be configured in appsettings.json, user secrets, or environment variables");

    return new Client(what =>
    {
        return what switch
        {
            "api_id" => options.ApiId,
            "api_hash" => options.ApiHash,
            "phone_number" => options.PhoneNumber,
            "verification_code" => null,
            "password" => options.Password,
            "session_pathname" => options.SessionPathname,
            _ => null
        };
    });
});

builder.Services.AddSingleton<TelegramService>();
builder.Services.AddScoped<ITempShareMessageDeleter, TelegramMessageDeleter>();
builder.Services.AddHostedService<TempShareCleanupService>();

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
    opts.PersistMessagesWithPostgresql(connectionString, "telecloud");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
}, ExtensionDiscovery.ManualOnly);

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();
builder.Services.AddHttpContextAccessor();
builder.Services.AddApiCookieAuth();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TeleCloudDbContext>();
    if (db.Database.GetPendingMigrations().Any())
        await db.Database.MigrateAsync();

    if (!await db.Folders.AnyAsync(f => f.Path == "/"))
    {
        db.Folders.Add(new Folder { Id = Guid.CreateVersion7(), Path = "/", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}

app.UseExceptionHandler();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints()
   .UseSwaggerGen();
app.MapGet("/", () => Results.Redirect("/swagger"));
await app.RunAsync();
