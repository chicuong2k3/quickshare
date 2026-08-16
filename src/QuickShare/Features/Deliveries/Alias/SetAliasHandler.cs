using System.Text.RegularExpressions;
using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Alias;

public record SetAliasCommand(Guid DeliveryId, string? Alias);

/// <summary>
/// Memorable per-share slug (issue #311): /d/my-project-2026 next to the generated code.
/// Paid plans only; the alias is an addressing convenience, never a security boundary —
/// password/OTP gates still apply on lookup.
/// </summary>
public sealed partial class SetAliasHandler
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]*[a-z0-9]$")]
    private static partial Regex AliasPattern();

    /// <summary>Slugs that collide with real routes or invite impersonation.</summary>
    public static readonly string[] ReservedAliases =
    [
        "admin", "api", "account", "login", "logout", "send", "pricing", "upgrade",
        "abuse", "terms", "privacy", "p2p", "tools", "d", "not-found", "quickshare",
        "support", "help", "download", "unsubscribe",
    ];

    public async Task<Result> Handle(
        SetAliasCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CacheService cache,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        string? alias = null;
        if (!string.IsNullOrWhiteSpace(cmd.Alias))
        {
            alias = cmd.Alias.Trim().ToLowerInvariant();
            if (alias.Length is < Delivery.AliasMinLength or > Delivery.AliasMaxLength
                || !AliasPattern().IsMatch(alias)
                || ReservedAliases.Contains(alias))
            {
                return Result.Failure(new LightResults.Error(ErrorCodes.AliasInvalid));
            }

            var taken = await db.Deliveries
                .AsNoTracking()
                .AnyAsync(d => d.Alias == alias && d.Id != delivery.Id, ct);
            if (taken)
                return Result.Failure(new LightResults.Error(ErrorCodes.AliasTaken));
        }

        delivery.Alias = alias;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique-index race: two senders claimed the same alias between check and save.
            return Result.Failure(new LightResults.Error(ErrorCodes.AliasTaken));
        }

        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success();
    }
}
