using System.Net;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class IpAddressSafetyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::")]
    public void PrivateReservedAddresses_AreBlocked(string ip)
    {
        Assert.True(IpAddressSafety.IsBlocked(IPAddress.Parse(ip)), $"{ip} should be blocked");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddresses_AreAllowed(string ip)
    {
        Assert.False(IpAddressSafety.IsBlocked(IPAddress.Parse(ip)), $"{ip} should be allowed");
    }
}
