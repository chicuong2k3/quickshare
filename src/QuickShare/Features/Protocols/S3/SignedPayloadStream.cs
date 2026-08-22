using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Features.Protocols.S3;

/// <summary>
/// The request body, with the promise its signature made about it enforced (issue #265).
///
/// A SigV4 header signs a *hash* of the payload, not the payload, so a signature that verifies
/// proves nothing about the bytes unless the bytes are checked against that hash. Skipping the
/// check would leave every upload body unauthenticated while the request looked signed — the
/// most comfortable kind of hole, because everything passes.
///
/// Both shapes are handled here because both arrive in practice: rclone sends the body straight
/// through with a hash (or declares it unsigned), and the AWS CLI sends <c>aws-chunked</c>
/// framing where each chunk carries its own signature chained from the one before it.
///
/// It is a stream rather than a buffer on purpose: a mount upload is a file, and reading one
/// into memory to check it would put the largest thing the plan allows into the heap.
/// </summary>
public static class SignedPayload
{
    /// <summary>Wraps the body so that the declared hash or chunk chain is enforced as it is read.</summary>
    public static Stream Wrap(Stream body, SigV4Request auth, byte[] signingKey) => auth.PayloadHash switch
    {
        SigV4.UnsignedPayload => body,
        SigV4.StreamingUnsigned => new AwsChunkedStream(body, null, auth),
        SigV4.StreamingSigned => new AwsChunkedStream(body, signingKey, auth),
        _ => new HashCheckedStream(body, auth.PayloadHash),
    };
}

/// <summary>
/// Passes the body through and fails at the end if the SHA-256 does not match what was signed.
///
/// The failure lands after the bytes have been written, which is why every caller deletes the
/// object it just wrote when this throws: better a stored object briefly orphaned (the sweeper,
/// #107, owns those) than a body nobody authenticated kept as if it were fine.
/// </summary>
internal sealed class HashCheckedStream(Stream inner, string expectedHash) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await inner.ReadAsync(buffer, ct);

        if (read > 0)
        {
            _hash.AppendData(buffer.Span[..read]);
            return read;
        }

        var actual = SigV4.Hex(_hash.GetHashAndReset());
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new MountPayloadException("Body does not match the signed content hash.");

        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Decodes <c>aws-chunked</c> framing, verifying each chunk's signature when there is one.
///
/// The chain is the point: every chunk signs the signature of the chunk before it, starting from
/// the seed signature in the Authorization header. Verifying the frames without the chain would
/// let anything be spliced in mid-body; verifying nothing would make the framing decoration.
/// </summary>
internal sealed class AwsChunkedStream(Stream inner, byte[]? signingKey, SigV4Request signed) : Stream
{
    private const string ChunkAlgorithm = "AWS4-HMAC-SHA256-PAYLOAD";
    private const string SignaturePrefix = ";chunk-signature=";
    /// <summary>A chunk is client-chosen; this is the ceiling before the framing is called a lie.</summary>
    private const int MaxChunkSize = 32 * 1024 * 1024;

    private string _previousSignature = signed.Signature;
    private byte[] _chunk = [];
    private int _offset;
    private bool _finished;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_offset >= _chunk.Length)
        {
            if (_finished || !await NextChunkAsync(ct))
                return 0;
        }

        var count = Math.Min(buffer.Length, _chunk.Length - _offset);
        _chunk.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;

        return count;
    }

    private async Task<bool> NextChunkAsync(CancellationToken ct)
    {
        var header = await ReadLineAsync(ct);
        if (header is null)
        {
            _finished = true;
            return false;
        }

        var signatureAt = header.IndexOf(SignaturePrefix, StringComparison.Ordinal);
        var sizeText = signatureAt < 0 ? header : header[..signatureAt];
        var chunkSignature = signatureAt < 0 ? null : header[(signatureAt + SignaturePrefix.Length)..].Trim();

        if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size)
            || size < 0 || size > MaxChunkSize)
        {
            throw new MountPayloadException("Malformed chunk header.");
        }

        var data = new byte[size];
        await inner.ReadExactlyAsync(data, ct);

        if (signingKey is not null)
            VerifyChunk(data, chunkSignature);

        // Each chunk — including the terminating zero-length one — is followed by CRLF.
        await ReadLineAsync(ct);

        if (size == 0)
        {
            _finished = true;
            return false;
        }

        _chunk = data;
        _offset = 0;

        return true;
    }

    private void VerifyChunk(byte[] data, string? chunkSignature)
    {
        if (string.IsNullOrEmpty(chunkSignature))
            throw new MountPayloadException("Chunk is missing its signature.");

        var stringToSign = string.Join('\n',
            ChunkAlgorithm,
            signed.AmzDate,
            signed.Scope,
            _previousSignature,
            SigV4.EmptyPayloadHash,
            SigV4.Hex(SHA256.HashData(data)));

        var expected = SigV4.Hex(SigV4.HmacSha256(signingKey!, stringToSign));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(chunkSignature.ToLowerInvariant())))
        {
            throw new MountPayloadException("Chunk signature does not match.");
        }

        _previousSignature = expected;
    }

    /// <summary>One CRLF-terminated line, read a byte at a time — headers here are tens of bytes.</summary>
    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var builder = new StringBuilder();
        var single = new byte[1];

        while (true)
        {
            var read = await inner.ReadAsync(single.AsMemory(0, 1), ct);
            if (read == 0)
                return builder.Length == 0 ? null : builder.ToString();

            if (single[0] == (byte)'\n')
                return builder.ToString().TrimEnd('\r');

            builder.Append((char)single[0]);

            if (builder.Length > 512)
                throw new MountPayloadException("Chunk header is implausibly long.");
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
