namespace TeleCloud.Web.Infrastructure;

public class CookieForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CookieForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var cookies = _httpContextAccessor.HttpContext?.Request.Headers.Cookie.ToString();
        if (!string.IsNullOrEmpty(cookies))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", cookies);
        }
        return base.SendAsync(request, ct);
    }
}
