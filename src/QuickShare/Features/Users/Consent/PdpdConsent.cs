using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Consent;

/// <summary>
/// Consent versions, and what each one covered (issue #230).
///
/// A consent is only meaningful against the text that was shown. Bumping the version when the
/// wording changes is what makes an old record honest — it says "this person agreed to *that*",
/// not "this person agreed to whatever we say today".
/// </summary>
public static class ConsentKinds
{
    /// <summary>Vietnam's Decree 13/2023 (PDPD): processing, and transfer abroad.</summary>
    public const string Pdpd = "pdpd";

    /// <summary>Bump when the consent text changes; old records keep naming the version they saw.</summary>
    public const string PdpdVersion = "2026-08-pdpd-v1";
}

public sealed record ConsentStatusDto(bool Granted, string Version, DateTime? GrantedAt);

/// <summary>
/// GET /api/consents/pdpd — whether this account has agreed to the current text (issue #230).
/// </summary>
public sealed class GetPdpdConsentEndpoint : EndpointWithoutRequest<ConsentStatusDto>
{
    public override void Configure()
    {
        Get("/api/consents/pdpd");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Whether the account has granted PDPD consent";
            s.Description = "Issue #230. Consent is versioned: a record for an older text does not count as consent to the current one.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var record = await db.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == user.Id && c.Kind == ConsentKinds.Pdpd && c.RevokedAt == null)
            .OrderByDescending(c => c.GrantedAt)
            .FirstOrDefaultAsync(ct);

        var current = record?.Version == ConsentKinds.PdpdVersion;

        await Send.OkAsync(new ConsentStatusDto(current, ConsentKinds.PdpdVersion, record?.GrantedAt), ct);
    }
}

/// <summary>
/// POST /api/consents/pdpd — record agreement to the current text (issue #230).
///
/// The record keeps the version and the moment, and nothing else about the person that we did
/// not already hold: a consent log that quietly becomes a second profile is its own privacy
/// problem. The IP is hashed, like everywhere else that records one.
/// </summary>
public sealed class GrantPdpdConsentEndpoint : EndpointWithoutRequest<ConsentStatusDto>
{
    public override void Configure()
    {
        Post("/api/consents/pdpd");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Record PDPD consent for the current account");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var existing = await db.UserConsents
            .Where(c => c.UserId == user.Id && c.Kind == ConsentKinds.Pdpd && c.Version == ConsentKinds.PdpdVersion)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            // Re-granting an active consent is a no-op rather than a second row: a log with one
            // entry per page load says nothing about when somebody actually agreed.
            existing.RevokedAt = null;
            await db.SaveChangesAsync(ct);
            await Send.OkAsync(new ConsentStatusDto(true, ConsentKinds.PdpdVersion, existing.GrantedAt), ct);
            return;
        }

        var record = new UserConsent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Kind = ConsentKinds.Pdpd,
            Version = ConsentKinds.PdpdVersion,
            GrantedAt = DateTime.UtcNow,
            IpHash = DeviceTrackerService.HashIp(HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty),
        };

        db.UserConsents.Add(record);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ConsentStatusDto(true, record.Version, record.GrantedAt), ct);
    }
}

/// <summary>
/// DELETE /api/consents/pdpd — withdraw it (issue #230).
///
/// Withdrawal is a right, so it is a control rather than a support ticket. The row is marked
/// revoked instead of deleted: "they consented on Tuesday and withdrew on Friday" is the record
/// that matters, and deleting it would erase the half that protects the person.
/// </summary>
public sealed class RevokePdpdConsentEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/consents/pdpd");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Withdraw PDPD consent");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var records = await db.UserConsents
            .Where(c => c.UserId == user.Id && c.Kind == ConsentKinds.Pdpd && c.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var record in records)
            record.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
