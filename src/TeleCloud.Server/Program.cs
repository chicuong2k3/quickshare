using Microsoft.Extensions.Options;
using FastEndpoints.Swagger;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");

builder.Services.AddDbContext<TeleCloudDbContext>(opts =>
    opts.UseNpgsql(connectionString));

builder.Services.Configure<TeleCloudOptions>(
    builder.Configuration.GetSection(TeleCloudOptions.Section));

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

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "telecloud");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
});

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();
builder.Services.AddHttpContextAccessor();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TeleCloudDbContext>();
    if (db.Database.GetPendingMigrations().Any())
        await db.Database.MigrateAsync();
}

app.UseExceptionHandler();

app.UseFastEndpoints()
   .UseSwaggerGen();
app.MapGet("/", () => Results.Redirect("/swagger"));
await app.RunAsync();
