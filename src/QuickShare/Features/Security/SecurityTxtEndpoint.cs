using System.Globalization;

namespace QuickShare.Features.Security;

/// <summary>
/// RFC 9116 security.txt — the industry-standard vulnerability disclosure
/// discovery file (issue #56). Served as plain text at the /.well-known/ path.
/// </summary>
public sealed class SecurityTxtEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(SecurityConstants.SecurityTxtPath);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Admin));
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";

        var expires = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var content = string.Join('\n',
            $"Contact: mailto:{SecurityConstants.ContactEmail}",
            $"Expires: {expires}",
            "Preferred-Languages: en, vi",
            $"Policy: {baseUrl}{SecurityConstants.DisclosurePolicyPath}",
            $"Canonical: {baseUrl}{SecurityConstants.SecurityTxtPath}",
            string.Empty);

        await Send.StringAsync(content, StatusCodes.Status200OK, "text/plain; charset=utf-8", ct);
    }
}
