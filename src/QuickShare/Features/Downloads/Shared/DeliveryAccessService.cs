using System.Text.Json;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Downloads.Shared;

/// <summary>Rule snapshot for one delivery — cached per share code, never per caller.</summary>
public sealed record DeliveryAccessRules(
    Guid DeliveryId,
    Guid? SenderId,
    string FileName,
    string? AllowedIpRanges,
    string? AllowedCountries,
    string? BlockedCountries)
{
    public bool HasRules =>
        !string.IsNullOrWhiteSpace(AllowedIpRanges)
        || !string.IsNullOrWhiteSpace(AllowedCountries)
        || !string.IsNullOrWhiteSpace(BlockedCountries);
}

/// <summary>
/// Applies a delivery's scope rules to the live request and audits refusals
/// (issue #142 CIDR allowlist, issue #166 country allow/deny). Every recipient-facing
/// content path routes through here so one rule set governs the page, the byte
/// stream, and every preview — and so a blocked attempt is always recorded.
///
/// The caller address comes from <c>Connection.RemoteIpAddress</c>, which
/// <c>UseForwardedHeaders</c> has already rewritten from X-Forwarded-For, and the
/// country from the Cloudflare edge header — the same source product analytics uses.
/// Refusals are attributed to the sender's security timeline: the recipient is
/// anonymous, and the sender is who needs to see rejects.
///
/// The rule snapshot is cached by share code only. A per-caller verdict must never
/// enter a shared cache, so evaluation always runs against the live request.
/// </summary>
public sealed class DeliveryAccessService(
    AppDbContext db,
    CacheService cache,
    IHttpContextAccessor http,
    AnalyticsService analytics,
    ILogger<DeliveryAccessService> logger)
{
    private static readonly TimeSpan RulesTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Evaluates the rules on an already-loaded delivery. Returns
    /// <see cref="DeliveryAccessDecision.Allowed"/> immediately when the delivery carries
    /// no rules at all — the common case, with no audit and no writes.
    /// </summary>
    public Task<DeliveryAccessDecision> CheckAsync(Delivery delivery, CancellationToken ct) =>
        CheckAsync(
            new DeliveryAccessRules(
                delivery.Id,
                delivery.SenderId,
                delivery.FileName,
                delivery.AllowedIpRanges,
                delivery.AllowedCountries,
                delivery.BlockedCountries),
            ct);

    /// <summary>Evaluates the rules for a share code, loading only the rule columns.</summary>
    public async Task<DeliveryAccessDecision> CheckByCodeAsync(string shareCode, CancellationToken ct)
    {
        var rules = await cache.GetOrSetAsync(
            $"download:access:{shareCode}",
            async ct2 => await db.Deliveries
                .AsNoTracking()
                .ByCode(shareCode)
                .Select(d => new DeliveryAccessRules(
                    d.Id,
                    d.SenderId,
                    d.FileName,
                    d.AllowedIpRanges,
                    d.AllowedCountries,
                    d.BlockedCountries))
                .FirstOrDefaultAsync(ct2),
            duration: RulesTtl,
            token: ct);

        if (rules is null)
            return DeliveryAccessDecision.Allowed;

        return await CheckAsync(rules, ct);
    }

    private async Task<DeliveryAccessDecision> CheckAsync(DeliveryAccessRules rules, CancellationToken ct)
    {
        if (!rules.HasRules)
            return DeliveryAccessDecision.Allowed;

        var context = http.HttpContext;
        var decision = DeliveryAccessPolicy.Evaluate(
            rules.AllowedIpRanges,
            rules.AllowedCountries,
            rules.BlockedCountries,
            context?.Connection.RemoteIpAddress,
            context?.Request.Headers[AnalyticsConstants.CountryHeader].FirstOrDefault());

        if (decision != DeliveryAccessDecision.Allowed)
            await AuditAsync(rules, decision, ct);

        return decision;
    }

    private async Task AuditAsync(DeliveryAccessRules rules, DeliveryAccessDecision decision, CancellationToken ct)
    {
        // Audit is observability, never a gate: a failure here must not turn a
        // refusal into a 500 (or, worse, an accidental allow).
        try
        {
            var eventName = decision == DeliveryAccessDecision.IpNotAllowed
                ? AnalyticsEvents.DeliveryAccessBlockedIp
                : AnalyticsEvents.DeliveryAccessBlockedCountry;

            await analytics.RecordAsync(eventName, new AnalyticsEventData(FileName: rules.FileName), ct);

            if (rules.SenderId is not { } senderId)
                return;

            var context = http.HttpContext;
            var metadata = JsonSerializer.Serialize(new
            {
                deliveryId = rules.DeliveryId,
                reason = decision.ToString(),
            });

            await SecurityEventService.AppendAsync(
                db,
                senderId,
                SecurityEventType.DeliveryAccessBlocked,
                context?.Request.Headers.UserAgent.ToString() ?? string.Empty,
                context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                ct,
                metadata);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to audit blocked access for delivery {DeliveryId}", rules.DeliveryId);
        }
    }

    /// <summary>Drops the cached rule snapshot after a sender edits the rules.</summary>
    public ValueTask InvalidateAsync(string shareCode, CancellationToken ct) =>
        cache.RemoveAsync($"download:access:{shareCode}", ct);
}
