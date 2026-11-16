using System.Security.Cryptography;

namespace QuickShare.Infrastructure;

/// <summary>URL-safe random share codes — shared by upload commit and delivery clone.</summary>
public static class ShareCodes
{
    private const int RandomByteCount = 8;

    public static string Generate()
    {
        var randomBytes = new byte[RandomByteCount];
        RandomNumberGenerator.Fill(randomBytes);
        return Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
