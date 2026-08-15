using Microsoft.AspNetCore.DataProtection;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class SenderEmailTokensTests
{
    [Fact]
    public void ConfirmToken_RoundTrips_DeliveryId()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var token = tokens.CreateConfirmToken(id);
        var ok = tokens.TryReadConfirmToken(token, out var read, out _);

        Assert.True(ok);
        Assert.Equal(id, read);
    }

    [Fact]
    public void ConfirmToken_Expired_IsRejected()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var token = tokens.CreateConfirmToken(id, DateTimeOffset.UtcNow.AddHours(-1));
        var ok = tokens.TryReadConfirmToken(token, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ConfirmToken_Invalid_IsRejected()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);

        Assert.False(tokens.TryReadConfirmToken("garbage", out _, out _));
    }

    [Fact]
    public void ConfirmAndUnsubscribeTokens_DoNotInteroperate()
    {
        var provider = new TestDataProtectionProvider();
        var tokens = new SenderEmailTokens(provider);
        var id = Guid.NewGuid();

        var confirm = tokens.CreateConfirmToken(id);
        var unsubscribe = tokens.CreateUnsubscribeToken(id);

        Assert.False(tokens.TryReadUnsubscribeToken(confirm, out _, out _));
        Assert.False(tokens.TryReadConfirmToken(unsubscribe, out _, out _));
    }
}

/// <summary>Purpose-prefixed base64 protector: proves format logic (round-trip,
/// expiry, garbage rejection, purpose separation) without platform crypto.</summary>
internal sealed class TestDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose) =>
        new EphemeralDataProtector(purpose);
}

internal sealed class EphemeralDataProtector(string purpose) : IDataProtector
{
    public IDataProtector CreateProtector(string subPurpose) =>
        new EphemeralDataProtector(purpose + "/" + subPurpose);

    public byte[] Protect(byte[] plaintext)
        => System.Text.Encoding.UTF8.GetBytes(purpose + ":" + Convert.ToBase64String(plaintext));

    public byte[] Unprotect(byte[] protectedData)
    {
        var text = System.Text.Encoding.UTF8.GetString(protectedData);
        var prefix = purpose + ":";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            throw new System.Security.Cryptography.CryptographicException("Wrong purpose.");
        return Convert.FromBase64String(text[prefix.Length..]);
    }
}
