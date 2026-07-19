using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Security.Claims;
using QuickShare.Web;
using QuickShare.Web.Infrastructure;
using RizzyUI;

var builder = WebApplication.CreateBuilder(args);

// Shared DataProtection keyring with Server (both use same ApplicationName + KeyPath).
var dpPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Path.GetTempPath(), "QuickShare", "keys");
Directory.CreateDirectory(dpPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpPath))
    .SetApplicationName("QuickShare");

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
        options.Cookie.Name = AuthConstants.CookieName;
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

builder.Services.AddLocalization();

// RizzyUI services + LinearTheme (custom dark theme). Per RizzyUI README:
// https://github.com/JalexSocial/RizzyUI  ->  AddRizzyUI + RzThemeProvider.
// NOTE: 0.9.2-alpha does NOT ship app.UseRizzy() or a RzHeadOutlet component
// (the README is ahead of the release), so those are intentionally omitted;
// Alpine.js is vendored locally under wwwroot/lib/alpine/ instead.
builder.Services.AddRizzyUI(config =>
{
    config.DefaultTheme = new QuickShare.Web.Infrastructure.LinearTheme();
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
builder.Services.AddHttpClient(ApiClient.Name, c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<CookieForwardingHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClient.Name));

var app = builder.Build();

// Serve wwwroot/ (and optimized static assets if a manifest is present).
app.UseStaticFiles();

// Auth middleware must come before Antiforgery and MapRazorComponents.
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<QuickShare.Web.Infrastructure.AuthPageRedirectMiddleware>();

// Antiforgery middleware for Blazor SSR endpoints (required by RizzyUI).
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

app.MapPost("/account/update", async (HttpContext httpContext, IHttpClientFactory clientFactory, IWebHostEnvironment env, IAntiforgery antiforgery) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
        return Results.Json(new { error = "Not authenticated" }) as IResult;

    try { await antiforgery.ValidateRequestAsync(httpContext); }
    catch (AntiforgeryValidationException)
    {
        return Results.Json(new { error = "Session expired. Please refresh the page." });
    }

    var form = await httpContext.Request.ReadFormAsync();
    var name = form["name"].FirstOrDefault()?.Trim();

    if (string.IsNullOrWhiteSpace(name))
        return Results.Json(new { error = "Name is required" });

    string? avatarUrl = null;
    var avatarFile = form.Files.GetFile("avatar");
    if (avatarFile is not null && avatarFile.Length > 0)
    {
        if (avatarFile.Length > 5 * 1024 * 1024)
            return Results.Json(new { error = "Image must be under 5 MB" });

        var ext = Path.GetExtension(avatarFile.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var fileName = $"{Guid.NewGuid()}{ext}";
        var avatarsDir = Path.Combine(env.WebRootPath, "avatars");
        Directory.CreateDirectory(avatarsDir);
        var filePath = Path.Combine(avatarsDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await avatarFile.CopyToAsync(stream);

        avatarUrl = $"/avatars/{fileName}";
    }

    var client = clientFactory.CreateClient(ApiClient.Name);
    var response = await client.PutAsJsonAsync("/api/users/me/profile",
        new { Name = name, AvatarUrl = avatarUrl });

    if (!response.IsSuccessStatusCode)
        return Results.Json(new { error = "Failed to update profile" });

    var identity = httpContext.User.Identities.First();
    var claims = identity.Claims
        .Where(c => c.Type is not ClaimTypes.Name and not "avatar_url")
        .ToList();
    claims.Add(new Claim(ClaimTypes.Name, name));
    if (avatarUrl is not null)
        claims.Add(new Claim("avatar_url", avatarUrl));

    var newPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, identity.AuthenticationType));
    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, newPrincipal);

    return Results.Json(new { redirect = "/account" });
}).WithMetadata(SkipAntiforgeryValidation.Instance);

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

// Disables UseAntiforgery() validation for specific endpoints (e.g. file upload).
internal sealed class SkipAntiforgeryValidation : IAntiforgeryMetadata
{
    public static readonly SkipAntiforgeryValidation Instance = new();
    public bool RequiresValidation => false;
}
