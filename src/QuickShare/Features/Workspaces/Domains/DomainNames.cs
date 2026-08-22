using System.Text.RegularExpressions;

namespace QuickShare.Features.Workspaces.Domains;

/// <summary>
/// Hostname rules for a white-label domain (issue #81).
///
/// Deliberately narrow: what an agency types is a hostname, and everything that looks like more
/// than a hostname — a scheme, a port, a path, a wildcard — is a misunderstanding worth
/// correcting at the input rather than storing and half-honouring later.
/// </summary>
public static partial class DomainNames
{
    public const int MinLabels = 2;

    [GeneratedRegex(@"^(?!-)[a-z0-9-]{1,63}(?<!-)$")]
    private static partial Regex LabelPattern();

    /// <summary>Null when the input is not a plain hostname we are willing to serve under.</summary>
    public static string? Normalize(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
            return null;

        // A pasted URL is the common case, not an attack: take its host rather than refusing.
        if (value.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            value = parsed.Host;
        }

        // Trailing dot is the DNS root and means the same host; a port or path does not.
        value = value.TrimEnd('.');
        if (value.Length == 0 || value.Length > WorkspaceDomain.HostMaxLength)
            return null;

        if (value.Contains('/', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
            return null;

        var labels = value.Split('.');
        if (labels.Length < MinLabels)
            return null;

        return labels.All(label => LabelPattern().IsMatch(label)) ? value : null;
    }

    /// <summary>
    /// True when the host is the deployment's own address, or lives under it. Letting a
    /// workspace claim <c>quickshare.cloud</c> — or a subdomain of it — would hand one tenant
    /// the pages of everyone else on that host.
    /// </summary>
    public static bool IsCanonical(string host, string canonicalHost) =>
        !string.IsNullOrEmpty(canonicalHost)
        && (string.Equals(host, canonicalHost, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + canonicalHost, StringComparison.OrdinalIgnoreCase));
}
