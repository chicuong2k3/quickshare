using System.Security.Cryptography;
using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Workspaces.Domains;

public sealed record DomainDto(
    string Host,
    string VerificationToken,
    string CnameTarget,
    bool Verified,
    DateTime? VerifiedAt,
    DateTime? LastCheckedAt,
    string? LastError);

public sealed class DomainRequest
{
    public string? Host { get; set; }
}

/// <summary>GET /api/workspace/domain — the workspace's white-label hostname (issue #81).</summary>
public sealed class GetWorkspaceDomainEndpoint : EndpointWithoutRequest<DomainDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/domain");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace's custom domain";
            s.Description = "Workspace admins only. Includes the CNAME target and the last verification result.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var domain = await db.WorkspaceDomains.AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(domain is null ? null : Map(domain, Resolve<DomainVerifier>().CanonicalHost), ct);
    }

    internal static DomainDto Map(WorkspaceDomain domain, string canonicalHost) => new(
        domain.Host,
        domain.VerificationToken,
        canonicalHost,
        domain.IsVerified,
        domain.VerifiedAt,
        domain.LastCheckedAt,
        domain.LastError);
}

/// <summary>
/// PUT /api/workspace/domain — claim a hostname (issue #81).
///
/// Claiming is not owning: the row is stored unverified and nothing is served under it until
/// DNS agrees. Studio-only, because white-label is the tier that is sold, and because a
/// hostname is the one brand control that can affect somebody outside this workspace.
/// </summary>
public sealed class SetWorkspaceDomainEndpoint : Endpoint<DomainRequest, DomainDto>
{
    public override void Configure()
    {
        Put("/api/workspace/domain");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Claim a custom domain for the workspace";
            s.Description = "Workspace admins on the Studio plan. Stored unverified; point a CNAME at the returned target, then call verify.";
        });
    }

    public override async Task HandleAsync(DomainRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var ownerPlan = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == seat.WorkspaceId)
            .Select(w => w.Owner.Plan)
            .FirstOrDefaultAsync(ct);

        if (ownerPlan != PlanTierLimits.Studio)
        {
            ThrowError(ErrorCodes.DomainRequiresStudio, StatusCodes.Status403Forbidden);
            return;
        }

        var verifier = Resolve<DomainVerifier>();
        var host = DomainNames.Normalize(req.Host);
        if (host is null || DomainNames.IsCanonical(host, verifier.CanonicalHost))
        {
            ThrowError(ErrorCodes.DomainInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var takenElsewhere = await db.WorkspaceDomains
            .AnyAsync(d => d.Host == host && d.WorkspaceId != seat.WorkspaceId, ct);

        if (takenElsewhere)
        {
            ThrowError(ErrorCodes.DomainTaken, StatusCodes.Status409Conflict);
            return;
        }

        var domain = await db.WorkspaceDomains.FirstOrDefaultAsync(d => d.WorkspaceId == seat.WorkspaceId, ct);
        var previousHost = domain?.Host;

        if (domain is null)
        {
            domain = new WorkspaceDomain
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
                VerificationToken = NewToken(),
            };
            db.WorkspaceDomains.Add(domain);
        }
        else if (!string.Equals(domain.Host, host, StringComparison.Ordinal))
        {
            // A new hostname is a new claim: it inherits neither the old verification nor the
            // old token, or renaming would be a way to serve an unverified host.
            domain.VerifiedAt = null;
            domain.VerificationToken = NewToken();
        }

        domain.Host = host;
        domain.LastError = null;
        domain.LastCheckedAt = null;
        domain.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, host, previousHost, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceDomainChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { host }));

        await Send.OkAsync(GetWorkspaceDomainEndpoint.Map(domain, verifier.CanonicalHost), ct);
    }

    internal static async Task EvictAsync(
        CacheService cache, Guid workspaceId, string host, string? previousHost, CancellationToken ct)
    {
        await WorkspaceDomainReader.EvictAsync(cache, workspaceId, host, ct);

        if (previousHost is not null && !string.Equals(previousHost, host, StringComparison.Ordinal))
            await WorkspaceDomainReader.EvictAsync(cache, workspaceId, previousHost, ct);
    }

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(WorkspaceDomain.TokenLength / 2)).ToLowerInvariant();
}

/// <summary>
/// POST /api/workspace/domain/verify — ask DNS whether the claim holds (issue #81).
///
/// Re-runnable on purpose: the first attempt usually fails because DNS has not propagated, and
/// "try again in a minute" is the whole support conversation this button replaces.
/// </summary>
public sealed class VerifyWorkspaceDomainEndpoint : EndpointWithoutRequest<DomainDto>
{
    public override void Configure()
    {
        Post("/api/workspace/domain/verify");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Verify the workspace's custom domain";
            s.Description = "Workspace admins only. Resolves the claimed host and compares it with the deployment's own address.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var domain = await db.WorkspaceDomains.FirstOrDefaultAsync(d => d.WorkspaceId == seat.WorkspaceId, ct);
        if (domain is null)
        {
            ThrowError(ErrorCodes.DomainNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var verifier = Resolve<DomainVerifier>();
        var failure = await verifier.CheckAsync(domain.Host, ct);
        var wasVerified = domain.IsVerified;

        domain.LastCheckedAt = DateTime.UtcNow;
        domain.LastError = failure;
        // A host that stops pointing at us stops being verified: leaving it verified would keep
        // us minting links to an address that no longer reaches the files.
        domain.VerifiedAt = failure is null ? domain.VerifiedAt ?? DateTime.UtcNow : null;
        domain.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await WorkspaceDomainReader.EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, domain.Host, ct);

        if (failure is null && !wasVerified)
        {
            await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
                SecurityEventType.WorkspaceDomainVerified, HttpContext, ct,
                JsonSerializer.Serialize(new { domain.Host }));
        }

        await Send.OkAsync(GetWorkspaceDomainEndpoint.Map(domain, verifier.CanonicalHost), ct);
    }
}

/// <summary>DELETE /api/workspace/domain — go back to the canonical address (issue #81).</summary>
public sealed class DeleteWorkspaceDomainEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/domain");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Remove the workspace's custom domain");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var domain = await db.WorkspaceDomains.FirstOrDefaultAsync(d => d.WorkspaceId == seat.WorkspaceId, ct);
        if (domain is null)
        {
            ThrowError(ErrorCodes.DomainNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var host = domain.Host;
        db.WorkspaceDomains.Remove(domain);
        await db.SaveChangesAsync(ct);
        await WorkspaceDomainReader.EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, host, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceDomainChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { removed = host }));

        await Send.NoContentAsync(ct);
    }
}
