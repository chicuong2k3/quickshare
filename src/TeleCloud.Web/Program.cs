using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using TeleCloud.Web;
using TeleCloud.Web.Infrastructure;
using RizzyUI;

var builder = WebApplication.CreateBuilder(args);

// Shared DataProtection keyring with Server (both use same ApplicationName + KeyPath).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "TeleCloud", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("TeleCloud");

// Blazor Static SSR.
// In .NET 10, Static Server rendering is the default for AddRazorComponents();
// the .AddStaticServerRendering() extension from .NET 8/9 has been removed.
builder.Services.AddRazorComponents();

// Antiforgery is required by Static SSR for form/POST scenarios.
builder.Services.AddAntiforgery();

// IHttpContextAccessor for auth state in Razor pages + CookieForwardingHandler.
builder.Services.AddHttpContextAccessor();

// Cookie auth + Google OAuth (GitHub, Facebook, TikTok temporarily disabled).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".AspNetCore.TeleCloud";
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Auth:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"]!;
        options.ClaimActions.MapJsonKey("avatar_url", "picture");
        options.Events.OnCreatingTicket = ctx =>
        {
            ctx.Identity?.AddClaim(new Claim("provider", "google"));
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// RizzyUI services + LinearTheme (custom dark theme). Per RizzyUI README:
// https://github.com/JalexSocial/RizzyUI  ->  AddRizzyUI + RzThemeProvider.
// NOTE: 0.9.2-alpha does NOT ship app.UseRizzy() or a RzHeadOutlet component
// (the README is ahead of the release), so those are intentionally omitted;
// Alpine.js is vendored locally under wwwroot/lib/alpine/ instead.
builder.Services.AddRizzyUI(config =>
{
    config.DefaultTheme = new TeleCloud.Web.Infrastructure.LinearTheme();
});

// HttpClient used by the SSR pages + CookieForwardingHandler.
// The browser-side upload JS also reads ApiBaseUrl from a page-emitted constant
// so the XHR goes directly to the API (CORS-enabled server-side).
builder.Services.AddTransient<CookieForwardingHandler>();

var apiBaseUrl = builder.Configuration["QuickShare:ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = "http://localhost:59155";
}
builder.Services.AddHttpClient(TempShareClient.Name, c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<CookieForwardingHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(TempShareClient.Name));

var app = builder.Build();

// Serve wwwroot/ (and optimized static assets if a manifest is present).
app.UseStaticFiles();

// Auth middleware must come before Antiforgery and MapRazorComponents.
app.UseAuthentication();
app.UseAuthorization();

// Antiforgery middleware for the Static SSR pipeline.
app.UseAntiforgery();

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
    context.Response.Redirect("/");
});

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
