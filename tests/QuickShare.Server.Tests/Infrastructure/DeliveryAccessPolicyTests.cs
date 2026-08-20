using System.Net;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.Infrastructure;

/// <summary>
/// Scope control for a delivery (issue #142 CIDR allowlist, issue #166 country rules).
/// The invariant under test is fail-closed: a rule that exists must refuse anyone it
/// cannot positively match, including a caller whose origin cannot be determined.
/// </summary>
public sealed class DeliveryAccessPolicyTests
{
    private const string Office = "203.0.113.0/24";

    private static DeliveryAccessDecision Evaluate(
        string? ranges = null,
        string? allowedCountries = null,
        string? blockedCountries = null,
        string? ip = null,
        string? country = null) =>
        DeliveryAccessPolicy.Evaluate(
            ranges,
            allowedCountries,
            blockedCountries,
            ip is null ? null : IPAddress.Parse(ip),
            country);

    [Fact]
    public void NoRules_AllowsEveryone()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ip: "8.8.8.8", country: "US"));
    }

    [Theory]
    [InlineData("203.0.113.1")]
    [InlineData("203.0.113.255")]
    public void AddressInsideAllowedRange_Passes(string ip)
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: Office, ip: ip));
    }

    [Theory]
    [InlineData("203.0.114.1")]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.112.255")]
    public void AddressOutsideAllowedRange_IsRefused(string ip)
    {
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: Office, ip: ip));
    }

    [Fact]
    public void BareAddressRule_MatchesOnlyThatHost()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: "198.51.100.7", ip: "198.51.100.7"));
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: "198.51.100.7", ip: "198.51.100.8"));
    }

    [Fact]
    public void MultipleRanges_MatchAnyOne()
    {
        const string ranges = "203.0.113.0/24,198.51.100.0/24";
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: ranges, ip: "198.51.100.9"));
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: ranges, ip: "192.0.2.9"));
    }

    [Fact]
    public void IPv6Range_IsMatchedByPrefix()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: "2001:db8::/32", ip: "2001:db8:1234::1"));
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: "2001:db8::/32", ip: "2001:db9::1"));
    }

    [Fact]
    public void IPv4MappedCaller_MatchesIPv4Rule()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: Office, ip: "::ffff:203.0.113.5"));
    }

    [Fact]
    public void RangeOfWrongFamily_NeverMatches()
    {
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: "2001:db8::/32", ip: "203.0.113.1"));
    }

    [Fact]
    public void UnknownCallerAddress_IsRefusedWhenRangeRuleExists()
    {
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: Office));
    }

    [Fact]
    public void MalformedRange_DoesNotOpenTheLink()
    {
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: "not-a-cidr", ip: "8.8.8.8"));
        Assert.Equal(DeliveryAccessDecision.IpNotAllowed, Evaluate(ranges: "203.0.113.0/99", ip: "203.0.113.1"));
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("US,VN", "vn")]
    [InlineData(" us , vn ", "US")]
    public void CountryOnAllowList_Passes(string allowed, string caller)
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(allowedCountries: allowed, country: caller));
    }

    [Fact]
    public void CountryOffAllowList_IsRefused()
    {
        Assert.Equal(DeliveryAccessDecision.CountryNotAllowed, Evaluate(allowedCountries: "DE,FR", country: "US"));
    }

    [Fact]
    public void CountryOnDenyList_IsRefused()
    {
        Assert.Equal(DeliveryAccessDecision.CountryNotAllowed, Evaluate(blockedCountries: "RU,KP", country: "kp"));
    }

    [Fact]
    public void CountryNotOnDenyList_Passes()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(blockedCountries: "RU,KP", country: "VN"));
    }

    [Fact]
    public void DenyListWins_OverAllowList()
    {
        Assert.Equal(
            DeliveryAccessDecision.CountryNotAllowed,
            Evaluate(allowedCountries: "US,RU", blockedCountries: "RU", country: "RU"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX1")]
    [InlineData("X")]
    public void UnresolvableCountry_IsRefusedWhenCountryRuleExists(string? country)
    {
        Assert.Equal(DeliveryAccessDecision.CountryNotAllowed, Evaluate(allowedCountries: "US", country: country));
        Assert.Equal(DeliveryAccessDecision.CountryNotAllowed, Evaluate(blockedCountries: "RU", country: country));
    }

    [Fact]
    public void UnresolvableCountry_PassesWhenOnlyRangeRuleExists()
    {
        Assert.Equal(DeliveryAccessDecision.Allowed, Evaluate(ranges: Office, ip: "203.0.113.1"));
    }

    [Fact]
    public void RangeAndCountryRules_AreAndScoped()
    {
        Assert.Equal(
            DeliveryAccessDecision.Allowed,
            Evaluate(ranges: Office, allowedCountries: "US", ip: "203.0.113.1", country: "US"));

        Assert.Equal(
            DeliveryAccessDecision.IpNotAllowed,
            Evaluate(ranges: Office, allowedCountries: "US", ip: "8.8.8.8", country: "US"));

        Assert.Equal(
            DeliveryAccessDecision.CountryNotAllowed,
            Evaluate(ranges: Office, allowedCountries: "US", ip: "203.0.113.1", country: "DE"));
    }

    [Fact]
    public void NormalizeIpRanges_DropsGarbageAndDeDupes()
    {
        Assert.Equal("203.0.113.0/24", DeliveryAccessPolicy.NormalizeIpRanges(" 203.0.113.0/24 , nonsense , 203.0.113.0/24 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense,also-nonsense")]
    public void NormalizeIpRanges_CollapsesEmptyAndInvalidToNull(string? input)
    {
        Assert.Null(DeliveryAccessPolicy.NormalizeIpRanges(input));
    }

    [Fact]
    public void NormalizeCountries_UpperCasesAndDeDupes()
    {
        Assert.Equal("US,VN", DeliveryAccessPolicy.NormalizeCountries(" us , VN , Us "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("USA,V,1234")]
    public void NormalizeCountries_CollapsesEmptyAndInvalidToNull(string? input)
    {
        Assert.Null(DeliveryAccessPolicy.NormalizeCountries(input));
    }

    [Fact]
    public void NormalizedRules_RoundTripThroughEvaluate()
    {
        var ranges = DeliveryAccessPolicy.NormalizeIpRanges("203.0.113.0/24, junk");
        var countries = DeliveryAccessPolicy.NormalizeCountries("us, junk");

        Assert.Equal(
            DeliveryAccessDecision.Allowed,
            DeliveryAccessPolicy.Evaluate(ranges, countries, null, IPAddress.Parse("203.0.113.4"), "US"));
    }
}
