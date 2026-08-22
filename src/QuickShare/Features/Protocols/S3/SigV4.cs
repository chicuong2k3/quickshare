using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace QuickShare.Features.Protocols.S3;

/// <summary>What a signature said about itself, before it was checked.</summary>
public sealed record SigV4Request(
    string AccessKeyId,
    string Scope,
    string Region,
    string Date,
    string AmzDate,
    IReadOnlyList<string> SignedHeaders,
    string Signature,
    string PayloadHash);

/// <summary>
/// AWS Signature Version 4, the server half (issue #265).
///
/// This is the one credential in the product whose secret cannot be stored as a hash: SigV4 is a
/// shared-secret HMAC, so verifying a signature means recomputing it, which means holding the
/// same bytes the client holds. That is a property of the protocol, not a shortcut — see
/// <see cref="S3Credentials"/> for what is done about it.
///
/// Written by hand rather than pulled from the AWS SDK because the SDK signs, it does not
/// verify: there is no server-side verifier in it to call. What is ours is small and exactly
/// specified, and the parts that are easy to get quietly wrong — the payload actually matching
/// its declared hash, the chunk signature chain, a constant-time comparison — are the parts that
/// are the security of the feature.
/// </summary>
public static class SigV4
{
    public const string Algorithm = "AWS4-HMAC-SHA256";
    public const string Service = "s3";
    public const string Terminator = "aws4_request";
    public const string ContentSha256Header = "x-amz-content-sha256";
    public const string DateHeader = "x-amz-date";
    public const string UnsignedPayload = "UNSIGNED-PAYLOAD";
    public const string StreamingSigned = "STREAMING-AWS4-HMAC-SHA256-PAYLOAD";
    public const string StreamingUnsigned = "STREAMING-UNSIGNED-PAYLOAD-TRAILER";
    public const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>How far a client's clock may be out before its signature is refused.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(15);

    private const string AmzDateFormat = "yyyyMMddTHHmmssZ";

    /// <summary>Reads the Authorization header. Null when it is absent or not SigV4 at all.</summary>
    public static SigV4Request? Parse(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Algorithm, StringComparison.Ordinal))
            return null;

        string? credential = null;
        string? signedHeaders = null;
        string? signature = null;

        foreach (var part in header[Algorithm.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var name = part[..separator];
            var value = part[(separator + 1)..];

            if (name.Equals("Credential", StringComparison.Ordinal)) credential = value;
            else if (name.Equals("SignedHeaders", StringComparison.Ordinal)) signedHeaders = value;
            else if (name.Equals("Signature", StringComparison.Ordinal)) signature = value;
        }

        if (credential is null || signedHeaders is null || signature is null)
            return null;

        // {access key}/{date}/{region}/s3/aws4_request
        var credentialParts = credential.Split('/');
        if (credentialParts.Length != 5 || credentialParts[3] != Service || credentialParts[4] != Terminator)
            return null;

        var amzDate = request.Headers[DateHeader].ToString();
        if (string.IsNullOrEmpty(amzDate))
            return null;

        var payloadHash = request.Headers[ContentSha256Header].ToString();
        if (string.IsNullOrEmpty(payloadHash))
            payloadHash = UnsignedPayload;

        return new SigV4Request(
            credentialParts[0],
            string.Join('/', credentialParts[1..]),
            credentialParts[2],
            credentialParts[1],
            amzDate,
            signedHeaders.Split(';', StringSplitOptions.RemoveEmptyEntries),
            signature,
            payloadHash);
    }

    /// <summary>
    /// True when the signature was produced by this secret, for this request, recently.
    ///
    /// The freshness check is not decoration: without it a captured request stays replayable for
    /// as long as the credential lives, and the signature itself says nothing about when it was
    /// made.
    /// </summary>
    public static bool Verify(HttpRequest request, SigV4Request auth, string secret, DateTime utcNow)
    {
        if (!DateTime.TryParseExact(auth.AmzDate, AmzDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var requestTime))
        {
            return false;
        }

        if ((utcNow - requestTime).Duration() > MaxClockSkew)
            return false;

        // The date inside the credential scope has to be the date the request claims: a scope
        // from another day would let one day's signing key sign forever.
        if (!auth.AmzDate.StartsWith(auth.Date, StringComparison.Ordinal))
            return false;

        var canonical = CanonicalRequest(request, auth);
        var stringToSign = string.Join('\n',
            Algorithm,
            auth.AmzDate,
            auth.Scope,
            Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));

        var expected = Hex(HmacSha256(SigningKey(secret, auth.Date, auth.Region), stringToSign));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(auth.Signature.ToLowerInvariant()));
    }

    /// <summary>The per-request signing key: date, region and service, in that order.</summary>
    public static byte[] SigningKey(string secret, string date, string region)
    {
        var key = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secret), date);
        key = HmacSha256(key, region);
        key = HmacSha256(key, Service);

        return HmacSha256(key, Terminator);
    }

    public static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    public static byte[] HmacSha256(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string CanonicalRequest(HttpRequest request, SigV4Request signed)
    {
        var (path, query) = RawTarget(request);

        var headers = new StringBuilder();
        foreach (var name in signed.SignedHeaders)
        {
            var value = name.Equals("host", StringComparison.OrdinalIgnoreCase)
                ? request.Host.Value ?? string.Empty
                : request.Headers[name].ToString();

            headers.Append(name.ToLowerInvariant()).Append(':').Append(Collapse(value)).Append('\n');
        }

        return string.Join('\n',
            request.Method,
            path,
            CanonicalQuery(query),
            headers.ToString(),
            string.Join(';', signed.SignedHeaders),
            signed.PayloadHash);
    }

    /// <summary>
    /// The path exactly as it arrived on the wire.
    ///
    /// S3 signs the *unnormalised* path, so re-encoding a decoded one would disagree with the
    /// client the moment a key contains a character the two encoders treat differently. The raw
    /// target is the only copy of what was actually signed.
    /// </summary>
    private static (string Path, string Query) RawTarget(HttpRequest request)
    {
        var raw = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (string.IsNullOrEmpty(raw))
            raw = request.Path.Value + request.QueryString.Value;

        var split = raw.IndexOf('?', StringComparison.Ordinal);

        return split < 0
            ? (raw, string.Empty)
            : (raw[..split], raw[(split + 1)..]);
    }

    /// <summary>Query parameters sorted by name then value, each already encoded by the client.</summary>
    private static string CanonicalQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
            return string.Empty;

        var pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                return separator < 0 ? (Key: pair, Value: string.Empty) : (Key: pair[..separator], Value: pair[(separator + 1)..]);
            })
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal);

        return string.Join('&', pairs.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>Trimmed, with runs of spaces collapsed — the canonicalisation the spec asks for.</summary>
    private static string Collapse(string value)
    {
        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;

        foreach (var c in trimmed)
        {
            if (c == ' ' && lastWasSpace)
                continue;

            lastWasSpace = c == ' ';
            builder.Append(c);
        }

        return builder.ToString();
    }
}
