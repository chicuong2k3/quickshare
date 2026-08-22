using System.Net;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Workspaces.Domains;

/// <summary>
/// Decides whether a workspace really controls the hostname it claims (issue #81).
///
/// The check is DNS resolution, not a TXT record: for the domain to work at all the agency has
/// to point it at us, so "does it already resolve where we live?" asks for exactly the setup
/// step they must perform anyway, and asks for nothing extra. A TXT record would prove
/// ownership of the *zone* — more than we need, and one more thing to get wrong.
///
/// ponytail: stdlib resolution, no DNS client dependency. If a deployment ever sits behind
/// addresses the canonical host does not share (split-horizon DNS, per-tenant anycast), this is
/// the seam to swap for a TXT lookup — the callers only ever see verified/not.
/// </summary>
public class DomainVerifier(IOptions<EmailOptions> emailOptions, ILogger<DomainVerifier> logger)
{
    /// <summary>The host an agency has to point their CNAME at. Shown on the setup screen.</summary>
    public virtual string CanonicalHost =>
        Uri.TryCreate(emailOptions.Value.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    /// <summary>Null when the host is ours to serve; otherwise the reason it is not, for the admin.</summary>
    public virtual async Task<string?> CheckAsync(string host, CancellationToken ct)
    {
        var canonical = CanonicalHost;
        if (string.IsNullOrEmpty(canonical))
            return "No canonical host is configured for this deployment.";

        try
        {
            var claimed = await Dns.GetHostAddressesAsync(host, ct);
            if (claimed.Length == 0)
                return $"{host} does not resolve yet.";

            var ours = await Dns.GetHostAddressesAsync(canonical, ct);
            var shared = claimed
                .Select(a => a.ToString())
                .Intersect(ours.Select(a => a.ToString()), StringComparer.OrdinalIgnoreCase)
                .Any();

            return shared ? null : $"{host} does not point at {canonical} yet.";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            // A resolution failure is "not yet", not an outage worth a 500: the admin has just
            // published a record and DNS has not caught up, which is the normal first attempt.
            logger.LogInformation(ex, "Domain verification lookup failed for {Host}", host);
            return $"{host} could not be resolved.";
        }
    }
}
