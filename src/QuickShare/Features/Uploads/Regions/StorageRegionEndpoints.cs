using QuickShare.Extensions;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Uploads.Regions;

public sealed record StorageRegionDto(string Code, string Label);

public sealed record StorageRegionOptionsDto(
    bool Supported,
    bool CanChoose,
    bool CanRoutePerDelivery,
    string? Preferred,
    IReadOnlyList<StorageRegionDto> Regions);

public sealed class SetPreferredRegionRequest
{
    /// <summary>Region code, or null/empty to go back to the default bucket.</summary>
    public string? Region { get; set; }
}

/// <summary>
/// GET /api/storage/regions — where this account may store deliveries (issue #119).
///
/// Reports the deployment's capability alongside the caller's entitlement, because those are
/// different questions: an operator may not have configured any region at all, in which case no
/// plan can choose one and the upload form should not offer a control that does nothing.
/// </summary>
public sealed class GetStorageRegionsEndpoint : EndpointWithoutRequest<StorageRegionOptionsDto>
{
    public override void Configure()
    {
        Get("/api/storage/regions");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "List available storage regions";
            s.Description = "Pro pins one region on the account; Studio may route per delivery. Empty when the deployment has no regions configured.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var regions = Resolve<StorageRegionResolver>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        await Send.OkAsync(new StorageRegionOptionsDto(
            regions.SupportsResidency,
            regions.SupportsResidency && StorageRegionResolver.CanChooseRegion(user.Plan),
            regions.SupportsResidency && StorageRegionResolver.CanRoutePerDelivery(user.Plan),
            user.PreferredStorageRegion,
            regions.AvailableRegions.Select(r => new StorageRegionDto(r.Code, r.Label)).ToList()), ct);
    }
}

/// <summary>
/// PUT /api/storage/regions/preferred — pin this account's uploads to a region (issue #119).
///
/// Existing deliveries are not moved. Relocating stored bytes across jurisdictions is a copy,
/// a verification and a delete on someone else's infrastructure, and doing it silently as a
/// side effect of a settings change is the opposite of what a residency control is for. The
/// setting takes effect on the next upload, and the page says so.
/// </summary>
public sealed class SetPreferredRegionEndpoint : Endpoint<SetPreferredRegionRequest, StorageRegionOptionsDto>
{
    public override void Configure()
    {
        Put("/api/storage/regions/preferred");
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Set the account's preferred storage region";
            s.Description = "Paid plans only. Applies to future uploads; deliveries already stored stay where they are.";
        });
    }

    public override async Task HandleAsync(SetPreferredRegionRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var regions = Resolve<StorageRegionResolver>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        if (!regions.SupportsResidency || !StorageRegionResolver.CanChooseRegion(user.Plan))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        var requested = req.Region?.Trim();
        string? resolved = null;

        if (!string.IsNullOrEmpty(requested))
        {
            resolved = regions.AvailableRegions.FirstOrDefault(r => r.Code == requested)?.Code;
            if (resolved is null)
            {
                ThrowError(ErrorCodes.StorageRegionInvalid, StatusCodes.Status400BadRequest);
                return;
            }
        }

        var now = DateTime.UtcNow;
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.PreferredStorageRegion, resolved)
                .SetProperty(u => u.UpdatedAt, now), ct);

        // GetUserAsync caches the account across requests; without this the next upload would
        // still resolve against the old preference.
        await UserCacheInvalidator.EvictAsync(db, user.Id, ct);

        await Send.OkAsync(new StorageRegionOptionsDto(
            regions.SupportsResidency,
            true,
            StorageRegionResolver.CanRoutePerDelivery(user.Plan),
            resolved,
            regions.AvailableRegions.Select(r => new StorageRegionDto(r.Code, r.Label)).ToList()), ct);
    }
}
