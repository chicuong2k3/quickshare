using Microsoft.AspNetCore.Antiforgery;
using TeleCloud.Web;
using RizzyUI;

var builder = WebApplication.CreateBuilder(args);

// Blazor Static SSR.
// In .NET 10, Static Server rendering is the default for AddRazorComponents();
// the .AddStaticServerRendering() extension from .NET 8/9 has been removed.
builder.Services.AddRazorComponents();

// Antiforgery is required by Static SSR for form/POST scenarios.
builder.Services.AddAntiforgery();

// RizzyUI services + ArcticTheme (default). Per RizzyUI README on GitHub:
// https://github.com/JalexSocial/RizzyUI  ->  AddRizzyUI + RzThemeProvider.
// NOTE: 0.9.2-alpha does NOT ship app.UseRizzy() or a RzHeadOutlet component
// (the README is ahead of the release), so those are intentionally omitted;
// Alpine.js is vendored locally under wwwroot/lib/alpine/ instead.
builder.Services.AddRizzyUI(config =>
{
    config.DefaultTheme = RzTheme.ArcticTheme;
});

// HttpClient used by the Download page (Static SSR) to call the backend API.
// The browser-side upload JS also reads ApiBaseUrl from a page-emitted constant
// so the XHR goes directly to the API (CORS-enabled server-side).
var apiBaseUrl = builder.Configuration["QuickShare:ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = "http://localhost:59155";
}
builder.Services.AddHttpClient(TempShareClient.Name, c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(TempShareClient.Name));

var app = builder.Build();

// Serve wwwroot/ (and optimized static assets if a manifest is present).
app.UseStaticFiles();

// Antiforgery middleware for the Static SSR pipeline.
app.UseAntiforgery();

// Map the root Blazor component (Static SSR — no rendermode helper).
app.MapRazorComponents<App>();

// Dedicated antiforgery token endpoint (good practice for SSR apps that POST).
app.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
});

// Optimized static asset manifest (no-op if manifest absent).
app.MapStaticAssets();

app.Run();
