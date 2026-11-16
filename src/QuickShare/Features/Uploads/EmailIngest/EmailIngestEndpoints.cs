using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Uploads.EmailIngest;

/// <summary>The account's own forwarding address (issue #104).</summary>
public sealed record EmailIngestAddressDto(string Address, bool Enabled);

/// <summary>
/// GET /api/uploads/email-address — where this account forwards files to (issue #104).
///
/// Minted on first request rather than at signup: an address nobody has asked for is an inbox
/// nobody watches, and every account having one would mean every account having a live ingest
/// route whether they wanted it or not.
/// </summary>
public sealed class GetEmailIngestAddressEndpoint : EndpointWithoutRequest<EmailIngestAddressDto>
{
    public override void Configure()
    {
        Get("/api/uploads/email-address");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "The account's private upload-by-email address";
            s.Description = "Issue #104. Created on first call. Only mail from the account's own address is accepted.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var options = Resolve<IOptions<EmailOptions>>().Value;
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        if (string.IsNullOrWhiteSpace(options.InboundDomain))
        {
            // Nothing is minted when the deployment cannot receive mail: an address that no
            // server accepts is a support ticket waiting to be filed.
            await Send.OkAsync(new EmailIngestAddressDto(string.Empty, Enabled: false), ct);
            return;
        }

        var address = await Resolve<EmailIngestService>().AddressForAsync(user, options.InboundDomain, ct);
        await Send.OkAsync(new EmailIngestAddressDto(address, Enabled: true), ct);
    }
}

/// <summary>
/// POST /api/uploads/inbound — the mail provider's parsed-mail webhook (issue #104).
///
/// Anonymous by necessity: the caller is a mail service, not a person. What stands in for
/// authentication is a shared secret compared in constant time, and the endpoint answers 200 to
/// almost everything afterwards — an unmatched address, a stranger's From, an empty mail. A
/// provider retries anything that is not a 200, and none of those are conditions retrying fixes.
/// </summary>
public sealed class InboundEmailEndpoint : Endpoint<InboundEmailRequest>
{
    public const string SecretHeader = "X-QuickShare-Inbound-Secret";

    public override void Configure()
    {
        Post("/api/uploads/inbound");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Receive a forwarded email and turn its attachments into deliveries";
            s.Description = "Issue #104. Requires the deployment's inbound secret. Mail from anybody but the address's owner is accepted and ignored.";
        });
    }

    public override async Task HandleAsync(InboundEmailRequest req, CancellationToken ct)
    {
        var options = Resolve<IOptions<EmailOptions>>().Value;

        if (string.IsNullOrEmpty(options.InboundSecret) || !SecretMatches(options.InboundSecret))
        {
            ThrowError(ErrorCodes.InboundUnauthorized, StatusCodes.Status401Unauthorized);
            return;
        }

        var created = await Resolve<EmailIngestService>().IngestAsync(req, ct);

        await Send.OkAsync(new { created }, ct);
    }

    private bool SecretMatches(string expected)
    {
        var provided = HttpContext.Request.Headers[SecretHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}
