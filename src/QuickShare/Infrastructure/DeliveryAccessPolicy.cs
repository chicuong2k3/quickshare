using System.Net;
using System.Net.Sockets;

namespace QuickShare.Infrastructure;

/// <summary>Why a recipient was refused a delivery by its scope rules.</summary>
public enum DeliveryAccessDecision
{
    Allowed,
    /// <summary>Caller address is outside the sender's CIDR allowlist (issue #142).</summary>
    IpNotAllowed,
    /// <summary>Caller country is outside the allow-list or on the deny-list (issue #166).</summary>
    CountryNotAllowed,
}

/// <summary>
/// Scope control for a delivery: a CIDR allowlist (issue #142) and country
/// allow/deny rules (issue #166). Both rule sets are AND-scoped — a caller must
/// satisfy every configured rule, and rules left unset never refuse anyone.
///
/// Rules are stored as comma-separated text on the delivery so a link carries its
/// own policy; there is no workspace table to join and no per-request lookup.
/// A malformed rule is treated as "does not match" rather than "matches
/// everything": a typo must never silently open a restricted link.
/// </summary>
public static class DeliveryAccessPolicy
{
    public const char RuleSeparator = ',';
    public const int CountryCodeLength = 2;
    private const int MaxRules = 64;
    private const int IPv4Bits = 32;
    private const int IPv6Bits = 128;

    /// <summary>
    /// Evaluates every configured rule for one caller. <paramref name="country"/> is the
    /// edge-provided ISO 3166-1 alpha-2 code (CF-IPCountry); when it is absent and a
    /// country rule exists the caller is refused — an unverifiable origin must not pass
    /// a rule that exists precisely to verify origin.
    /// </summary>
    public static DeliveryAccessDecision Evaluate(
        string? allowedIpRanges,
        string? allowedCountries,
        string? blockedCountries,
        IPAddress? callerIp,
        string? country)
    {
        if (!string.IsNullOrWhiteSpace(allowedIpRanges) && !MatchesAnyRange(allowedIpRanges, callerIp))
            return DeliveryAccessDecision.IpNotAllowed;

        var hasAllowList = !string.IsNullOrWhiteSpace(allowedCountries);
        var hasDenyList = !string.IsNullOrWhiteSpace(blockedCountries);

        if (!hasAllowList && !hasDenyList)
            return DeliveryAccessDecision.Allowed;

        var normalized = NormalizeCountry(country);
        if (normalized is null)
            return DeliveryAccessDecision.CountryNotAllowed;

        if (hasAllowList && !ContainsCountry(allowedCountries!, normalized))
            return DeliveryAccessDecision.CountryNotAllowed;

        if (hasDenyList && ContainsCountry(blockedCountries!, normalized))
            return DeliveryAccessDecision.CountryNotAllowed;

        return DeliveryAccessDecision.Allowed;
    }

    /// <summary>
    /// Normalizes a sender-supplied CIDR list for storage: trims, de-dupes, drops
    /// unparseable entries, caps the count. Returns null when nothing valid remains,
    /// so "no rule" and "only garbage" collapse to the same unrestricted state.
    /// </summary>
    public static string? NormalizeIpRanges(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var kept = new List<string>();
        foreach (var raw in input.Split(RuleSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (kept.Count >= MaxRules)
                break;

            if (!TryParseRange(raw, out _, out _))
                continue;

            if (!kept.Contains(raw, StringComparer.OrdinalIgnoreCase))
                kept.Add(raw);
        }

        return kept.Count == 0 ? null : string.Join(RuleSeparator, kept);
    }

    /// <summary>
    /// Normalizes a sender-supplied country list for storage: upper-cases, keeps only
    /// two-letter alphabetic codes, de-dupes, caps the count. Null when nothing valid remains.
    /// </summary>
    public static string? NormalizeCountries(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var kept = new List<string>();
        foreach (var raw in input.Split(RuleSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (kept.Count >= MaxRules)
                break;

            var code = NormalizeCountry(raw);
            if (code is null)
                continue;

            if (!kept.Contains(code, StringComparer.Ordinal))
                kept.Add(code);
        }

        return kept.Count == 0 ? null : string.Join(RuleSeparator, kept);
    }

    private static string? NormalizeCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        if (trimmed.Length != CountryCodeLength)
            return null;

        if (!char.IsAsciiLetter(trimmed[0]) || !char.IsAsciiLetter(trimmed[1]))
            return null;

        return trimmed.ToUpperInvariant();
    }

    private static bool ContainsCountry(string list, string normalizedCountry)
    {
        foreach (var raw in list.Split(RuleSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(raw, normalizedCountry, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MatchesAnyRange(string ranges, IPAddress? callerIp)
    {
        // No resolvable caller address cannot satisfy an address rule.
        if (callerIp is null)
            return false;

        var caller = Unmap(callerIp);

        foreach (var raw in ranges.Split(RuleSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseRange(raw, out var network, out var prefixLength))
                continue;

            if (IsInNetwork(caller, network, prefixLength))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses "203.0.113.0/24", "2001:db8::/32", or a bare address (treated as a
    /// single-host range). Rejects an out-of-family or out-of-range prefix length.
    /// </summary>
    private static bool TryParseRange(string rule, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        var slash = rule.IndexOf('/');
        var addressPart = slash < 0 ? rule : rule[..slash];

        if (!IPAddress.TryParse(addressPart, out var parsed))
            return false;

        network = Unmap(parsed);
        var maxBits = network.AddressFamily == AddressFamily.InterNetworkV6 ? IPv6Bits : IPv4Bits;

        if (slash < 0)
        {
            prefixLength = maxBits;
            return true;
        }

        if (!int.TryParse(rule[(slash + 1)..], out prefixLength))
            return false;

        return prefixLength >= 0 && prefixLength <= maxBits;
    }

    private static IPAddress Unmap(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool IsInNetwork(IPAddress caller, IPAddress network, int prefixLength)
    {
        if (caller.AddressFamily != network.AddressFamily)
            return false;

        Span<byte> callerBytes = stackalloc byte[16];
        Span<byte> networkBytes = stackalloc byte[16];

        if (!caller.TryWriteBytes(callerBytes, out var callerLength))
            return false;
        if (!network.TryWriteBytes(networkBytes, out var networkLength))
            return false;
        if (callerLength != networkLength)
            return false;

        var fullBytes = prefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (callerBytes[i] != networkBytes[i])
                return false;
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (callerBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
