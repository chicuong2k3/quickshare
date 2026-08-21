using Microsoft.Extensions.Options;

namespace QuickShare.Features.Uploads.Shared;

/// <summary>
/// Decides which storage location an upload is pinned to (issue #119).
///
/// The tiering follows what the plans promise: **Studio routes per delivery**, because an agency
/// holds clients under different regimes and the EU contract and the VN one land in different
/// buckets. **Pro picks a region once** on the account, and every upload follows it — a single
/// region is what that plan sells, and honouring a per-request override would quietly give Pro
/// the Studio feature. **Free and anonymous** get the default bucket, and asking them to choose
/// would be offering a control that does nothing.
///
/// An unknown or unconfigured region code is not an error. Residency is a deployment
/// capability: an operator who has not configured regions should see uploads keep working, not
/// a validation failure on a control the UI never showed.
/// </summary>
public sealed class StorageRegionResolver(IOptions<ObjectStoreOptions> options)
{
    private readonly ObjectStoreOptions _options = options.Value;

    public bool SupportsResidency => _options.SupportsResidency;

    public IReadOnlyList<StorageRegionOption> AvailableRegions => _options.UsableRegions;

    /// <summary>Whether a plan may pick a region at all.</summary>
    public static bool CanChooseRegion(string plan) =>
        plan is PlanTierLimits.Pro or PlanTierLimits.Studio;

    /// <summary>Whether a plan may vary the region per delivery, rather than once per account.</summary>
    public static bool CanRoutePerDelivery(string plan) =>
        plan == PlanTierLimits.Studio;

    /// <summary>
    /// The region code to stamp on this upload, or null for the default bucket. Never throws:
    /// see the class remarks on why an unusable choice degrades instead of failing.
    /// </summary>
    public string? Resolve(User? user, string plan, string? requestedRegion)
    {
        if (!_options.SupportsResidency || user is null || !CanChooseRegion(plan))
            return null;

        var requested = CanRoutePerDelivery(plan) ? requestedRegion : null;
        var chosen = requested ?? user.PreferredStorageRegion;

        return _options.FindRegion(chosen)?.Code;
    }

    /// <summary>The human label for a stored region, or null when it is not (or no longer) configured.</summary>
    public string? LabelFor(string? code) => _options.FindRegion(code)?.Label;
}
