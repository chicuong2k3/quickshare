namespace QuickShare.Features.Ads.CookieConsent;

public sealed class CookieConsentEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post(RoutePaths.CookieConsent);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Ads));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
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

            await Send.OkAsync(cancellation: ct);
        }
    }

    private const string QueryParamConsent = "consent";
}
