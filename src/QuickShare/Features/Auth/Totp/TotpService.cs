using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickShare.Features.Auth.Totp;

/// <summary>
/// RFC 6238 TOTP, computed with the BCL (issue #128).
///
/// No dependency: the algorithm is an HMAC over a counter and a modulo, and
/// <see cref="HMACSHA1"/> ships in the framework. A library here would be a supply-chain edge
/// and a version to track for about forty lines of arithmetic that has not changed since 2011.
///
/// SHA-1 is not a shortcut — RFC 6238 specifies HMAC-SHA-1 as the default, and it is what Google
/// Authenticator, Authy and 1Password implement. HMAC-SHA-1 is unaffected by the collision
/// attacks that retired bare SHA-1.
/// </summary>
public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int RecoveryCodeBytes = 8;

    /// <summary>A fresh base32 secret, ready to be shown as a QR code.</summary>
    public static string GenerateSecret()
    {
        var buffer = RandomNumberGenerator.GetBytes(UserTotp.SecretLength * 5 / 8);
        var builder = new StringBuilder(UserTotp.SecretLength);

        // Base32 packs 5 bits per character; walk the bit stream rather than the byte stream.
        var bits = 0;
        var value = 0;
        foreach (var b in buffer)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            builder.Append(Base32Alphabet[(value << (5 - bits)) & 31]);

        return builder.ToString()[..UserTotp.SecretLength];
    }

    /// <summary>The `otpauth://` URI an authenticator app scans.</summary>
    public static string BuildUri(string secret, string accountEmail, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountEmail}");
        return $"otpauth://totp/{label}"
            + $"?secret={secret}"
            + $"&issuer={Uri.EscapeDataString(issuer)}"
            + $"&algorithm=SHA1&digits={UserTotp.DigitCount}&period={UserTotp.PeriodSeconds}";
    }

    /// <summary>The time step a moment falls in. Exposed so replay checks can compare steps.</summary>
    public static long StepFor(DateTime utcNow) =>
        new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / UserTotp.PeriodSeconds;

    /// <summary>The code for one specific step. Public for tests, which must generate valid codes.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 specifies HMAC-SHA-1 for TOTP, and it is what every authenticator app implements. "
            + "HMAC-SHA-1 is not affected by the collision attacks that retired bare SHA-1; changing it here would "
            + "produce codes no authenticator can generate.")]
    public static string ComputeCode(string secret, long step)
    {
        var key = FromBase32(secret);
        var counter = new byte[8];
        BinaryPrimitivesWriteInt64BigEndian(counter, step);

        var hash = HMACSHA1.HashData(key, counter);

        // Dynamic truncation, RFC 4226 §5.3: the low nibble of the last byte picks the offset.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, UserTotp.DigitCount);
        return (binary % modulo).ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(UserTotp.DigitCount, '0');
    }

    /// <summary>
    /// Verifies a code against the steps around <paramref name="utcNow"/>, and returns the step
    /// it matched so the caller can record it. A step at or below <paramref name="lastUsedStep"/>
    /// is refused: a code is single-use, or one shoulder-surfed six digits stays live for the
    /// rest of its window.
    /// </summary>
    public static bool TryVerify(string secret, string code, DateTime utcNow, long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        var candidate = code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (candidate.Length != UserTotp.DigitCount)
            return false;

        var current = StepFor(utcNow);
        for (var drift = -UserTotp.AllowedDriftSteps; drift <= UserTotp.AllowedDriftSteps; drift++)
        {
            var step = current + drift;
            if (step <= lastUsedStep)
                continue;

            // Fixed-time compare: the codes are short and an attacker controls the guess.
            var expected = ComputeCode(secret, step);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(candidate)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fresh recovery codes: the raw values for the account holder to save, and the hashes to
    /// store. A lost phone with no way back in is how people avoid enabling this at all.
    /// </summary>
    public static (IReadOnlyList<string> Codes, string HashesJson) GenerateRecoveryCodes()
    {
        var codes = new List<string>(UserTotp.RecoveryCodeCount);
        var hashes = new List<string>(UserTotp.RecoveryCodeCount);

        for (var i = 0; i < UserTotp.RecoveryCodeCount; i++)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(RecoveryCodeBytes)).ToLowerInvariant();
            codes.Add(code);
            hashes.Add(HashRecoveryCode(code));
        }

        return (codes, JsonSerializer.Serialize(hashes));
    }

    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToLowerInvariant())))
            .ToLowerInvariant();

    /// <summary>
    /// Spends a recovery code if it is present, returning the remaining hashes. Single-use: a
    /// code that let somebody in once must not let them in again.
    /// </summary>
    public static bool TrySpendRecoveryCode(string hashesJson, string code, out string remainingJson)
    {
        remainingJson = hashesJson;
        var hashes = JsonSerializer.Deserialize<List<string>>(hashesJson) ?? [];
        var candidate = HashRecoveryCode(code);

        if (!hashes.Remove(candidate))
            return false;

        remainingJson = JsonSerializer.Serialize(hashes);
        return true;
    }

    public static int CountRecoveryCodes(string hashesJson) =>
        (JsonSerializer.Deserialize<List<string>>(hashesJson) ?? []).Count;

    private static byte[] FromBase32(string input)
    {
        var output = new List<byte>(input.Length * 5 / 8);
        var bits = 0;
        var value = 0;

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var index = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (index < 0)
                continue;

            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }

    private static void BinaryPrimitivesWriteInt64BigEndian(byte[] destination, long value)
    {
        for (var i = 7; i >= 0; i--)
        {
            destination[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }
}
