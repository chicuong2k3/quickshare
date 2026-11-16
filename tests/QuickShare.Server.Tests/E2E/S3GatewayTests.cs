using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Protocols.S3;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The S3-compatible gateway (issue #265).
///
/// The suite signs its own requests, which is the only way to test a signature verifier that
/// means anything: a stub that "authenticates" would prove nothing about whether a real client
/// can talk to us, and nothing about whether a forged request cannot. What matters here is that
/// a good signature works, a bad one does not, a stale one does not, and a body that disagrees
/// with the hash it was signed against is refused rather than stored.
/// </summary>
public sealed class S3GatewayTests : IClassFixture<TestWebAppFactory>
{
    private const string Gateway = "/s3";
    private const string Region = "us-east-1";
    private static readonly XNamespace S3Ns = "http://s3.amazonaws.com/doc/2006-03-01/";

    private readonly TestWebAppFactory _factory;

    public S3GatewayTests(TestWebAppFactory factory) => _factory = factory;

    // ── Signatures ──

    [Fact]
    public async Task Gateway_RefusesAnUnsignedRequest()
    {
        var (_, _, _) = await GatewayAsync();

        var response = await _factory.CreateClient().GetAsync(Gateway);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("AccessDenied", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_RefusesAForgedSignature()
    {
        var (credential, _, _) = await GatewayAsync();

        var response = await SendAsync(HttpMethod.Get, Gateway, credential with { Secret = "not-the-secret" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_RefusesASignatureFromLastWeek()
    {
        var (credential, _, _) = await GatewayAsync();

        // Without a freshness check, a captured request stays replayable for the life of the key.
        var response = await SendAsync(HttpMethod.Get, Gateway, credential, when: DateTime.UtcNow.AddDays(-7));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_RefusesARevokedCredential()
    {
        var (credential, client, _) = await GatewayAsync();

        var listed = await client.GetFromJsonAsync<List<S3CredentialDto>>("/api/workspace/s3-credentials");
        var revoke = await client.DeleteAsync($"/api/workspace/s3-credentials/{listed!.Single().Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var response = await SendAsync(HttpMethod.Get, Gateway, credential);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Listing ──

    [Fact]
    public async Task ListBuckets_ReturnsTheWorkspaceAsOneBucket()
    {
        var (credential, _, bucket) = await GatewayAsync();

        var response = await SendAsync(HttpMethod.Get, Gateway, credential);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(bucket, xml.Descendants(S3Ns + "Name").Single().Value);
    }

    [Fact]
    public async Task ListObjects_ReturnsKeysIncludingFolderPaths()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);
        await PutAsync(credential, bucket, "top.txt", "a");
        await PutAsync(credential, bucket, "campaign/brief.txt", "b");

        var response = await SendAsync(HttpMethod.Get, $"{Gateway}/{bucket}?list-type=2", credential);
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = xml.Descendants(S3Ns + "Key").Select(e => e.Value).ToList();

        // Without a delimiter S3 is flat, which is what a backup tool syncing a tree expects.
        Assert.Contains("top.txt", keys);
        Assert.Contains("campaign/brief.txt", keys);
    }

    [Fact]
    public async Task ListObjects_CollapsesFoldersIntoCommonPrefixes()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);
        await PutAsync(credential, bucket, "campaign/brief.txt", "b");

        var response = await SendAsync(HttpMethod.Get, $"{Gateway}/{bucket}?delimiter=%2F", credential);
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());

        // How every client renders a folder it was never actually given.
        Assert.Contains("campaign/", xml.Descendants(S3Ns + "Prefix").Select(e => e.Value));
    }

    [Fact]
    public async Task Bucket_ThatIsNotTheCallersWorkspaceDoesNotExist()
    {
        var (credential, _, _) = await GatewayAsync();

        var response = await SendAsync(HttpMethod.Get, $"{Gateway}/somebody-elses-bucket", credential);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("NoSuchBucket", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ── Objects ──

    [Fact]
    public async Task PutAndGet_RoundTripsTheBytesAsADelivery()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var put = await PutAsync(credential, bucket, "notes.txt", "hello gateway");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await SendAsync(HttpMethod.Get, $"{Gateway}/{bucket}/notes.txt", credential);
        Assert.Equal("hello gateway", await get.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.FileName == "notes.txt");

        // It is an ordinary delivery: a link, an expiry, and the mount as its recorded source.
        Assert.Equal(DeliverySources.Mount, delivery.Source);
        Assert.NotEqual(string.Empty, delivery.ShareCode);
    }

    [Fact]
    public async Task Put_WithANestedKeyCreatesTheFoldersItNeeds()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var response = await PutAsync(credential, bucket, "client/project/report.txt", "x");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // S3 has no MKCOL — the path has to appear because the key said so.
        Assert.True(await db.WorkspaceFolders.AnyAsync(f => f.Name == "client"));
        Assert.True(await db.WorkspaceFolders.AnyAsync(f => f.Name == "project"));
    }

    [Fact]
    public async Task Put_RefusesAKeyNestedDeeperThanTheFolderModel()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var response = await PutAsync(credential, bucket, "a/b/c/too-deep.txt", "x");

        // Flattening would file somebody's object somewhere they did not ask for.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_RefusesABodyThatDoesNotMatchItsSignedHash()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        // The signature covers a hash of the payload, so a signature alone says nothing about
        // the bytes. Without this check every upload body would be unauthenticated.
        var response = await PutAsync(credential, bucket, "tampered.txt", "what was signed",
            actualBody: "what actually arrived");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("BadDigest", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Deliveries.AnyAsync(d => d.FileName == "tampered.txt"));
    }

    [Fact]
    public async Task Put_AcceptsAnAwsChunkedBodyWithASignedChunkChain()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        // What the AWS CLI actually sends: framed chunks, each signing the one before it.
        var response = await PutChunkedAsync(credential, bucket, "chunked.txt", "streamed content", corrupt: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = await SendAsync(HttpMethod.Get, $"{Gateway}/{bucket}/chunked.txt", credential);
        Assert.Equal("streamed content", await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_RefusesAnAwsChunkedBodyWhoseChainWasBroken()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var response = await PutChunkedAsync(credential, bucket, "spliced.txt", "streamed content", corrupt: true);

        // Verifying the framing without the chain would let anything be spliced mid-body.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Deliveries.AnyAsync(d => d.FileName == "spliced.txt"));
    }

    [Fact]
    public async Task Put_IsRefusedByAReadOnlyCredential()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read);

        var response = await PutAsync(credential, bucket, "nope.txt", "x");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NeedsManageAndIsSoft()
    {
        var (writer, client, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);
        await PutAsync(writer, bucket, "temp.txt", "x");

        var refused = await SendAsync(HttpMethod.Delete, $"{Gateway}/{bucket}/temp.txt", writer);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var manager = await IssueAsync(client, ApiScopes.Read + "," + ApiScopes.Write + "," + ApiScopes.Manage);
        var deleted = await SendAsync(HttpMethod.Delete, $"{Gateway}/{bucket}/temp.txt", manager);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.FileName == "temp.txt");
        Assert.NotNull(delivery.DeletedAt);
    }

    [Fact]
    public async Task Delete_OfSomethingThatIsNotThereSucceeds()
    {
        var (credential, _, bucket) = await GatewayAsync(
            ApiScopes.Read + "," + ApiScopes.Write + "," + ApiScopes.Manage);

        var response = await SendAsync(HttpMethod.Delete, $"{Gateway}/{bucket}/never-existed.txt", credential);

        // S3 says so, and every client relies on it.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Multipart_IsRefusedRatherThanHalfSupported()
    {
        var (credential, _, bucket) = await GatewayAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var response = await SendAsync(HttpMethod.Post, $"{Gateway}/{bucket}/big.bin?uploads", credential);

        // A gateway that accepted CreateMultipartUpload and then lost parts is worse than one
        // that says no.
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    // ── Credential management ──

    [Fact]
    public async Task Credentials_ReturnTheSecretExactlyOnce()
    {
        var (_, client, _) = await GatewayAsync();

        var listed = await client.GetFromJsonAsync<List<S3CredentialDto>>("/api/workspace/s3-credentials");

        var only = Assert.Single(listed!);
        Assert.Null(only.Secret);
        Assert.StartsWith(WorkspaceS3Credential.AccessKeyPrefix, only.AccessKeyId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeveloperPage_ShowsTheEndpointAndBucketToAnAdmin()
    {
        var (_, client, bucket) = await GatewayAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/developer");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // An endpoint nobody can find is the defect the batch-11 audit spent its time on.
        Assert.Contains(Gateway, html, StringComparison.Ordinal);
        Assert.Contains(bucket, html, StringComparison.Ordinal);
        Assert.Contains("dev.s3.create", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private sealed record Credential(string AccessKeyId, string Secret);

    private async Task<(Credential Credential, HttpClient Client, string Bucket)> GatewayAsync(
        string scopes = ApiScopes.Read)
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var name = $"Bucket {Guid.NewGuid():N}"[..16];
        var created = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = name, Seats = 3 });
        created.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Name == name);

        return (await IssueAsync(client, scopes), client, S3Endpoints.BucketName(workspace));
    }

    private static async Task<Credential> IssueAsync(HttpClient client, string scopes)
    {
        var response = await client.PostAsJsonAsync("/api/workspace/s3-credentials",
            new CreateS3CredentialRequest { Label = "gateway", Scopes = scopes });
        response.EnsureSuccessStatusCode();

        var dto = (await response.Content.ReadFromJsonAsync<S3CredentialDto>())!;

        return new Credential(dto.AccessKeyId, dto.Secret!);
    }

    private Task<HttpResponseMessage> PutAsync(
        Credential credential, string bucket, string key, string body, string? actualBody = null)
    {
        var signedHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var content = new StringContent(actualBody ?? body, Encoding.UTF8, "text/plain");

        return SendAsync(HttpMethod.Put, $"{Gateway}/{bucket}/{key}", credential, payloadHash: signedHash, content: content);
    }

    /// <summary>
    /// Builds an <c>aws-chunked</c> body by hand — the AWS CLI's upload shape. Each chunk signs
    /// the previous signature, starting from the seed in the Authorization header, so the whole
    /// body is covered rather than just its first frame.
    /// </summary>
    private async Task<HttpResponseMessage> PutChunkedAsync(
        Credential credential, string bucket, string key, string body, bool corrupt)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var date = amzDate[..8];
        var scope = $"{date}/{Region}/s3/aws4_request";
        var url = $"{Gateway}/{bucket}/{key}";

        var seed = Signature(credential, HttpMethod.Put.Method, url, amzDate, date, scope,
            SigV4.StreamingSigned, out var signingKey);

        var payload = Encoding.UTF8.GetBytes(body);
        var framed = new StringBuilder();
        var previous = seed;

        foreach (var chunk in new[] { payload, Array.Empty<byte>() })
        {
            var stringToSign = string.Join('\n',
                "AWS4-HMAC-SHA256-PAYLOAD", amzDate, scope, previous,
                SigV4.EmptyPayloadHash, Hex(SHA256.HashData(chunk)));

            var signature = Hex(SigV4.HmacSha256(signingKey, stringToSign));
            if (corrupt && chunk.Length > 0)
                signature = new string('0', signature.Length);

            framed.Append(chunk.Length.ToString("x", CultureInfo.InvariantCulture))
                .Append(";chunk-signature=").Append(signature).Append("\r\n")
                .Append(Encoding.UTF8.GetString(chunk)).Append("\r\n");

            previous = signature;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(framed.ToString(), Encoding.UTF8, "text/plain"),
        };

        Authorize(request, credential, amzDate, scope, SigV4.StreamingSigned, seed);

        return await _factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, Credential credential,
        DateTime? when = null, string? payloadHash = null, HttpContent? content = null)
    {
        var now = when ?? DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var date = amzDate[..8];
        var scope = $"{date}/{Region}/s3/aws4_request";
        var hash = payloadHash ?? SigV4.EmptyPayloadHash;

        var signature = Signature(credential, method.Method, url, amzDate, date, scope, hash, out _);

        using var request = new HttpRequestMessage(method, url) { Content = content };
        Authorize(request, credential, amzDate, scope, hash, signature);

        return await _factory.CreateClient().SendAsync(request);
    }

    /// <summary>The client half of SigV4, written out so the test proves interoperability.</summary>
    private static string Signature(
        Credential credential, string method, string url, string amzDate, string date, string scope,
        string payloadHash, out byte[] signingKey)
    {
        var split = url.IndexOf('?', StringComparison.Ordinal);
        var path = split < 0 ? url : url[..split];
        var query = split < 0 ? string.Empty : url[(split + 1)..];

        var canonicalQuery = string.Join('&', query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Contains('=', StringComparison.Ordinal) ? pair : pair + "=")
            .OrderBy(pair => pair, StringComparer.Ordinal));

        var canonical = string.Join('\n',
            method,
            path,
            canonicalQuery,
            $"host:localhost\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n",
            "host;x-amz-content-sha256;x-amz-date",
            payloadHash);

        var stringToSign = string.Join('\n',
            SigV4.Algorithm, amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));

        signingKey = SigV4.SigningKey(credential.Secret, date, Region);

        return Hex(SigV4.HmacSha256(signingKey, stringToSign));
    }

    private static void Authorize(
        HttpRequestMessage request, Credential credential, string amzDate, string scope,
        string payloadHash, string signature)
    {
        request.Headers.TryAddWithoutValidation("Authorization",
            $"{SigV4.Algorithm} Credential={credential.AccessKeyId}/{scope}, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
            $"Signature={signature}");
        request.Headers.TryAddWithoutValidation(SigV4.DateHeader, amzDate);
        request.Headers.TryAddWithoutValidation(SigV4.ContentSha256Header, payloadHash);
    }

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}
