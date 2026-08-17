using System.Net;
using System.Net.Sockets;

namespace QuickShare.Infrastructure;

public interface IRemoteUrlFetcher
{
    Task<RemoteUrlResult> FetchAsync(string url, long maxBytes, CancellationToken ct);
}

public sealed record RemoteUrlResult(
    RemoteUrlStatus Status,
    Stream? Content = null,
    string? ContentType = null,
    string? FileName = null);

public enum RemoteUrlStatus
{
    Ok,
    InvalidUrl,
    FetchFailed,
    TooLarge,
}

public sealed class RemoteUrlTooLargeException : Exception
{
    public RemoteUrlTooLargeException() : base("Remote content exceeds the size limit") { }
}

/// <summary>
/// Server-side remote fetch for upload-from-URL (issue #57). SSRF-safe: the connection
/// callback resolves the host and connects only to a public IP, rejecting private,
/// loopback, link-local, CGNAT and reserved ranges — so a URL pointing at an internal
/// service never reaches it.
/// </summary>
public sealed class RemoteUrlFetcher : IRemoteUrlFetcher
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public async Task<RemoteUrlResult> FetchAsync(string url, long maxBytes, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new RemoteUrlResult(RemoteUrlStatus.InvalidUrl);
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = ConnectTimeout,
            ConnectCallback = ConnectToPublicAddressAsync,
        };
        var client = new HttpClient(handler) { Timeout = RequestTimeout };

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch
        {
            handler.Dispose();
            client.Dispose();
            return new RemoteUrlResult(RemoteUrlStatus.FetchFailed);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            handler.Dispose();
            client.Dispose();
            return new RemoteUrlResult(RemoteUrlStatus.FetchFailed);
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
        {
            response.Dispose();
            handler.Dispose();
            client.Dispose();
            return new RemoteUrlResult(RemoteUrlStatus.TooLarge);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = DeriveFileName(uri);

        var stream = await response.Content.ReadAsStreamAsync(ct);
        var limited = new SizeLimitedStream(stream, maxBytes, response, client, handler);

        return new RemoteUrlResult(RemoteUrlStatus.Ok, limited, contentType, fileName);
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var target = addresses.FirstOrDefault(a => !IpAddressSafety.IsBlocked(a));
        if (target is null)
            throw new InvalidOperationException("Remote URL resolves to a blocked address");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(target, context.DnsEndPoint.Port, ct);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static string DeriveFileName(Uri uri)
    {
        var segment = uri.AbsolutePath.Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (string.IsNullOrWhiteSpace(segment))
            return "download";

        var decoded = Uri.UnescapeDataString(segment);
        return string.IsNullOrWhiteSpace(decoded) ? "download" : decoded;
    }

    private sealed class SizeLimitedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private readonly HttpResponseMessage _response;
        private readonly HttpClient _client;
        private readonly SocketsHttpHandler _handler;
        private long _total;

        public SizeLimitedStream(Stream inner, long maxBytes, HttpResponseMessage response, HttpClient client, SocketsHttpHandler handler)
        {
            _inner = inner;
            _maxBytes = maxBytes;
            _response = response;
            _client = client;
            _handler = handler;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _total += read;
            if (_total > _maxBytes) throw new RemoteUrlTooLargeException();
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            _total += read;
            if (_total > _maxBytes) throw new RemoteUrlTooLargeException();
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
                _client.Dispose();
                _handler.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
