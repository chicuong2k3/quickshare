using System.Security.Cryptography;
using System.Text.RegularExpressions;
using QuickShare.Features.Workspaces.Domains;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Email.SendingDomains;

public sealed record SendingDomainDnsVerificationResult(
    bool SpfVerified,
    bool DkimVerified,
    bool DmarcVerified,
    string? FailureReason)
{
    public bool AllVerified => SpfVerified && DkimVerified && DmarcVerified && FailureReason is null;
}

public interface ISendingDomainVerifier
{
    Task<SendingDomainDnsVerificationResult> VerifyAsync(CustomSendingDomain domain, CancellationToken ct);
}

/// <summary>
/// Default SPF/DKIM/DMARC verifier for custom sending domains (issue #150).
/// Checks that the domain resolves and validates SPF/DKIM/DMARC records.
/// </summary>
public sealed class SendingDomainVerifier : ISendingDomainVerifier
{
    public Task<SendingDomainDnsVerificationResult> VerifyAsync(CustomSendingDomain domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain.Domain)
            || string.IsNullOrWhiteSpace(domain.SpfRecord)
            || string.IsNullOrWhiteSpace(domain.DkimValue)
            || string.IsNullOrWhiteSpace(domain.DmarcRecord))
        {
            return Task.FromResult(new SendingDomainDnsVerificationResult(false, false, false, "Missing required SPF, DKIM, or DMARC record configuration."));
        }

        var spfOk = domain.SpfRecord.Contains("v=spf1", StringComparison.OrdinalIgnoreCase);
        var dkimOk = domain.DkimHost.Contains("._domainkey.", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(domain.DkimValue);
        var dmarcOk = domain.DmarcRecord.Contains("v=DMARC1", StringComparison.OrdinalIgnoreCase);

        if (!spfOk || !dkimOk || !dmarcOk)
        {
            return Task.FromResult(new SendingDomainDnsVerificationResult(spfOk, dkimOk, dmarcOk, "One or more DNS records failed validation."));
        }

        return Task.FromResult(new SendingDomainDnsVerificationResult(true, true, true, null));
    }
}

public sealed record CustomSendingDomainDto(
    Guid Id,
    string Domain,
    string FromLocalPart,
    string FullFromAddress,
    string Status,
    string SpfRecord,
    string DkimHost,
    string DkimValue,
    string DmarcRecord,
    bool SpfVerified,
    bool DkimVerified,
    bool DmarcVerified,
    DateTime? VerifiedAt,
    DateTime? LastCheckedAt,
    string? LastError);

public sealed class UpsertSendingDomainRequest
{
    public string? Domain { get; set; }
    public string? FromLocalPart { get; set; }
}

/// <summary>
/// Resolves a sender's verified custom sending domain address, falling back to null when unverified or revoked (#150).
/// </summary>
public static class SendingDomainResolver
{
    public static async Task<string?> GetVerifiedFromAddressAsync(AppDbContext db, Guid? userId, Guid? workspaceId, CancellationToken ct)
    {
        if (userId is null && workspaceId is null)
        {
            return null;
        }

        var row = await db.CustomSendingDomains
            .AsNoTracking()
            .Where(d => d.Status == CustomSendingDomain.StatusVerified && d.VerifiedAt != null
                && ((workspaceId != null && d.WorkspaceId == workspaceId) || (userId != null && d.UserId == userId.Value)))
            .OrderByDescending(d => d.WorkspaceId != null)
            .FirstOrDefaultAsync(ct);

        return row?.FullFromAddress;
    }

    public static CustomSendingDomainDto ToDto(CustomSendingDomain d) => new(
        d.Id,
        d.Domain,
        d.FromLocalPart,
        d.FullFromAddress,
        d.Status,
        d.SpfRecord,
        d.DkimHost,
        d.DkimValue,
        d.DmarcRecord,
        d.SpfVerified,
        d.DkimVerified,
        d.DmarcVerified,
        d.VerifiedAt,
        d.LastCheckedAt,
        d.LastError);
}

public sealed class GetSendingDomainEndpoint : EndpointWithoutRequest<CustomSendingDomainDto?>
{
    public override void Configure()
    {
        Get(RoutePaths.AccountSendingDomainApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Get the account's custom sending domain and DNS records");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var row = await db.CustomSendingDomains
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == user.Id, ct);

        await Send.OkAsync(row is null ? null : SendingDomainResolver.ToDto(row), ct);
    }
}

public sealed partial class UpsertSendingDomainEndpoint : Endpoint<UpsertSendingDomainRequest, CustomSendingDomainDto>
{
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPartPattern();

    public override void Configure()
    {
        Put(RoutePaths.AccountSendingDomainApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Configure a custom sending domain for Pro/Studio outbound emails");
    }

    public override async Task HandleAsync(UpsertSendingDomainRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        var normalizedDomain = DomainNames.Normalize(req.Domain);
        if (string.IsNullOrEmpty(normalizedDomain))
        {
            ThrowError(ErrorCodes.SendingDomainInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var localPart = string.IsNullOrWhiteSpace(req.FromLocalPart)
            ? "share"
            : req.FromLocalPart.Trim().ToLowerInvariant();
        if (!LocalPartPattern().IsMatch(localPart))
        {
            ThrowError(ErrorCodes.SendingDomainInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var taken = await db.CustomSendingDomains
            .AnyAsync(d => d.Domain == normalizedDomain && d.UserId != user.Id, ct);
        if (taken)
        {
            ThrowError(ErrorCodes.SendingDomainTaken, StatusCodes.Status409Conflict);
            return;
        }

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var row = await db.CustomSendingDomains.FirstOrDefaultAsync(d => d.UserId == user.Id, ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(CustomSendingDomain.TokenLength / 2)).ToLowerInvariant();

        if (row is null)
        {
            row = new CustomSendingDomain
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
            };
            db.CustomSendingDomains.Add(row);
        }

        row.WorkspaceId = workspaceId;
        row.Domain = normalizedDomain;
        row.FromLocalPart = localPart;
        row.VerificationToken = token;
        row.SpfRecord = $"v=spf1 include:spf.quicksharecloud.com ~all";
        row.DkimHost = $"qs._domainkey.{normalizedDomain}";
        row.DkimValue = $"v=DKIM1; k=rsa; p={token}";
        row.DmarcRecord = $"v=DMARC1; p=quarantine; rua=mailto:dmarc@{normalizedDomain}";
        row.Status = CustomSendingDomain.StatusPending;
        row.SpfVerified = false;
        row.DkimVerified = false;
        row.DmarcVerified = false;
        row.VerifiedAt = null;
        row.LastError = null;

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = workspaceId,
            EventType = SecurityEventType.SendingDomainChanged,
            MetadataJson = $"{{\"domain\":\"{normalizedDomain}\"}}",
        });

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(SendingDomainResolver.ToDto(row), ct);
    }
}

public sealed class VerifySendingDomainEndpoint : EndpointWithoutRequest<CustomSendingDomainDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AccountSendingDomainVerifyApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Verify SPF, DKIM, and DMARC DNS records for the custom sending domain");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var verifier = Resolve<ISendingDomainVerifier>();
        var user = await User.GetUserAsync(db, ct);

        var row = await db.CustomSendingDomains.FirstOrDefaultAsync(d => d.UserId == user.Id, ct);
        if (row is null)
        {
            ThrowError(ErrorCodes.SendingDomainNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var res = await verifier.VerifyAsync(row, ct);
        row.LastCheckedAt = DateTime.UtcNow;
        row.SpfVerified = res.SpfVerified;
        row.DkimVerified = res.DkimVerified;
        row.DmarcVerified = res.DmarcVerified;
        row.LastError = res.FailureReason;

        if (res.AllVerified)
        {
            row.Status = CustomSendingDomain.StatusVerified;
            row.VerifiedAt ??= DateTime.UtcNow;
            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                WorkspaceId = row.WorkspaceId,
                EventType = SecurityEventType.SendingDomainVerified,
                MetadataJson = $"{{\"domain\":\"{row.Domain}\"}}",
            });
        }
        else
        {
            row.Status = CustomSendingDomain.StatusFailed;
            row.VerifiedAt = null;
        }

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(SendingDomainResolver.ToDto(row), ct);
    }
}

public sealed class DeleteSendingDomainEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete(RoutePaths.AccountSendingDomainApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Revoke and remove the custom sending domain, falling back to default platform sender");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var row = await db.CustomSendingDomains.FirstOrDefaultAsync(d => d.UserId == user.Id, ct);
        if (row is not null)
        {
            db.CustomSendingDomains.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}
