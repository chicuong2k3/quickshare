using Microsoft.AspNetCore.Antiforgery;

namespace QuickShare.Features.Users.Onboarding;

public sealed class DismissOnboardingEndpoint : EndpointWithoutRequest
{
    private const string NoStore = "no-store";

    public override void Configure()
    {
        Post(RoutePaths.AccountOnboardingDismiss);
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            await Resolve<IAntiforgery>().ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        HttpContext.Response.Cookies.Append(OnboardingDismissal.CookieName, OnboardingDismissal.CookieValue, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = HttpContext.Request.IsHttps,
            Path = RoutePaths.Home,
            MaxAge = TimeSpan.FromDays(OnboardingDismissal.CookieDays),
        });
        HttpContext.Response.Headers.CacheControl = NoStore;
        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = RoutePaths.Account;
    }
}
