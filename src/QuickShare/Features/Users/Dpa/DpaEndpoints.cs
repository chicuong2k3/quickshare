using System.Text.Json;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Dpa;

public sealed record DpaStatusDto(
    string CurrentVersion,
    DateOnly CurrentVersionDate,
    bool IsSigned,
    string? SignedVersion,
    DateTime? SignedAt,
    string? CompanyName,
    string? SignerName,
    string? SignerEmail,
    bool NeedsResign);

public sealed class SignDpaRequest
{
    public string? CompanyName { get; set; }
    public string? SignerName { get; set; }
    public string? SignerEmail { get; set; }
}

/// <summary>
/// GET /api/dpa — whether this account has a countersigned data processing agreement
/// (issue #184).
///
/// <c>NeedsResign</c> is the field procurement actually cares about: a signature against an
/// older version of the terms is still a real agreement, but it is not agreement to the terms
/// we publish today, and pretending otherwise is the kind of thing that surfaces during a
/// vendor review rather than before one.
/// </summary>
public sealed class GetDpaStatusEndpoint : EndpointWithoutRequest<DpaStatusDto>
{
    public override void Configure()
    {
        Get("/api/dpa");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Get the caller's DPA signature status";
            s.Description = "Reports the current terms version, whether this account has signed, and whether the signature is against an older version.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var latest = await db.DpaSignatures
            .AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .OrderByDescending(d => d.SignedAt)
            .FirstOrDefaultAsync(ct);

        await Send.OkAsync(Map(latest), ct);
    }

    internal static DpaStatusDto Map(DpaSignature? signature) => new(
        DpaTerms.CurrentVersion,
        DpaTerms.CurrentVersionDate,
        signature is not null,
        signature?.Version,
        signature?.SignedAt,
        signature?.CompanyName,
        signature?.SignerName,
        signature?.SignerEmail,
        signature is not null && signature.Version != DpaTerms.CurrentVersion);
}

/// <summary>
/// POST /api/dpa/sign — countersign the current DPA (issue #184).
///
/// The signature is the row: who signed, for which company, against which version, when, and
/// from where. There is no separate signed artefact to keep in sync — the printable page
/// renders the stored record beside the published terms, so the evidence and the document
/// cannot disagree.
///
/// Signing the same version twice is refused rather than silently re-stamped: a second row
/// would make the "when did they agree" question ambiguous, which is the one question the
/// record exists to answer.
/// </summary>
public sealed class SignDpaEndpoint : Endpoint<SignDpaRequest, DpaStatusDto>
{
    public override void Configure()
    {
        Post("/api/dpa/sign");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Sign the current data processing agreement";
            s.Description = "Records the signer, company and timestamp against the published terms version. Signing the same version twice is refused.";
        });
    }

    public override async Task HandleAsync(SignDpaRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var company = Trim(req.CompanyName, DpaSignature.CompanyNameMaxLength);
        var signer = Trim(req.SignerName, DpaSignature.SignerMaxLength);
        // The signing address falls back to the account email rather than being demanded again:
        // the account is already the verified identity here.
        var email = Trim(req.SignerEmail, DpaSignature.SignerMaxLength);
        if (string.IsNullOrEmpty(email))
            email = user.Email;

        if (string.IsNullOrEmpty(company) || string.IsNullOrEmpty(signer) || string.IsNullOrEmpty(email))
        {
            ThrowError(ErrorCodes.DpaSignerRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var already = await db.DpaSignatures
            .AnyAsync(d => d.UserId == user.Id && d.Version == DpaTerms.CurrentVersion, ct);
        if (already)
        {
            ThrowError(ErrorCodes.DpaAlreadySigned, StatusCodes.Status409Conflict);
            return;
        }

        var signature = new DpaSignature
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Version = DpaTerms.CurrentVersion,
            CompanyName = company,
            SignerName = signer,
            SignerEmail = email,
            SignedAt = DateTime.UtcNow,
            IpHash = DeviceTrackerService.HashIp(HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty),
            CreatedAt = DateTime.UtcNow,
        };
        db.DpaSignatures.Add(signature);

        // Signing a contract belongs on the account's security timeline, not just in a table
        // nobody looks at until a dispute.
        await SecurityEventService.AppendAsync(
            db,
            user.Id,
            SecurityEventType.DpaSigned,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            ct,
            JsonSerializer.Serialize(new { signature.Version, signature.CompanyName }));

        await Send.OkAsync(GetDpaStatusEndpoint.Map(signature), ct);
    }

    private static string Trim(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
