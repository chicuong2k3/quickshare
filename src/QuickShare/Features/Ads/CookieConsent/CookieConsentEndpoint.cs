using Microsoft.AspNetCore.Antiforgery;

namespace QuickShare.Features.Ads.CookieConsent;

public sealed class CookieConsentEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post(RoutePaths.CookieConsent);
        AllowAnonymous();
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Ads));
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

        var consent = Query<string>(QueryParamConsent, isRequired: false);

        if (consent != AdConstants.ConsentCookieValueGranted && consent != AdConstants.ConsentCookieValueDenied)
        {
            ThrowError("Invalid consent value", StatusCodes.Status400BadRequest);
        }
        else
        {
            HttpContext.Response.Cookies.Append(AdConstants.ConsentCookieName, consent, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Secure = false
            });

            var request = HttpContext.Request;
            var returnPath = RoutePaths.Home;
            if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                && string.Equals(referer.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
                && referer.PathAndQuery.StartsWith('/')
                && !referer.PathAndQuery.StartsWith("//", StringComparison.Ordinal)
                && !referer.PathAndQuery.Contains('\\'))
            {
                returnPath = referer.PathAndQuery;
            }

            HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
            HttpContext.Response.Headers.Location = returnPath;
        }
    }

    private const string QueryParamConsent = "consent";
}
