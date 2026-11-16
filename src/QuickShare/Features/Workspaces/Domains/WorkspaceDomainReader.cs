using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Workspaces.Domains;

/// <summary>
/// Where a delivery's public link lives, and who owns the host a request arrived on (issue #81).
///
/// Both directions are cached, because every recipient page asks at least one of them and a
/// domain changes about as often as an agency rebrands. Only *verified* rows are ever returned:
/// an unverified claim must not affect a single byte anybody sees, or claiming a hostname would
/// be a way to break somebody else's links.
/// </summary>
public sealed class WorkspaceDomainReader(AppDbContext db, CacheService cache, IOptions<EmailOptions> emailOptions)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private static string HostKey(string host) => $"wsdomain:host:{host}";
    private static string WorkspaceKey(Guid workspaceId) => $"wsdomain:ws:{workspaceId}";

    public static async ValueTask EvictAsync(CacheService cache, Guid workspaceId, string host, CancellationToken ct)
    {
        await cache.RemoveAsync(WorkspaceKey(workspaceId), ct);
        await cache.RemoveAsync(HostKey(host), ct);
    }

    /// <summary>The workspace that owns this hostname, or null for the canonical host.</summary>
    public async Task<Guid?> WorkspaceForHostAsync(string? host, CancellationToken ct)
    {
        var normalized = DomainNames.Normalize(host);
        if (normalized is null || DomainNames.IsCanonical(normalized, CanonicalHost))
            return null;

        return await cache.GetOrSetAsync(
            HostKey(normalized),
            async token => await db.WorkspaceDomains.AsNoTracking()
                .Where(d => d.Host == normalized && d.VerifiedAt != null)
                .Select(d => (Guid?)d.WorkspaceId)
                .FirstOrDefaultAsync(token),
            duration: CacheDuration,
            token: ct);
    }

    /// <summary>
    /// True when this host belongs to a workspace and the delivery does not (issue #81).
    ///
    /// Every recipient-facing page asks this, not just the download page: an agency's client
    /// seeing somebody else's file — or somebody else's download statistics — under the agency's
    /// own hostname is the worst failure this feature has, and it was originally guarded on one
    /// page while `/d/{code}/stats` sat one route away.
    /// </summary>
    public async Task<bool> IsForeignHostAsync(string? host, Guid? deliveryWorkspaceId, CancellationToken ct)
    {
        var hostWorkspaceId = await WorkspaceForHostAsync(host, ct);

        return hostWorkspaceId is { } owner && deliveryWorkspaceId != owner;
    }

    /// <summary>The workspace's verified hostname, or null when it shares the canonical one.</summary>
    public async Task<string?> HostForWorkspaceAsync(Guid? workspaceId, CancellationToken ct)
    {
        if (workspaceId is not { } id)
            return null;

        return await cache.GetOrSetAsync(
            WorkspaceKey(id),
            async token => await db.WorkspaceDomains.AsNoTracking()
                .Where(d => d.WorkspaceId == id && d.VerifiedAt != null)
                .Select(d => d.Host)
                .FirstOrDefaultAsync(token),
            duration: CacheDuration,
            token: ct);
    }

    /// <summary>
    /// The absolute recipient link for a share. Every path that mails or displays one goes
    /// through here — a link that is branded in the confirmation email and unbranded in the
    /// expiry reminder is worse than no white-label at all.
    /// </summary>
    public async Task<string> DeliveryUrlAsync(Guid? workspaceId, string shareCode, CancellationToken ct)
    {
        var host = await HostForWorkspaceAsync(workspaceId, ct);
        var origin = host is null ? emailOptions.Value.BaseUrl.TrimEnd('/') : $"https://{host}";
        return $"{origin}{RoutePaths.DeliveryPage}{shareCode}";
    }

    private string CanonicalHost =>
        Uri.TryCreate(emailOptions.Value.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
}
