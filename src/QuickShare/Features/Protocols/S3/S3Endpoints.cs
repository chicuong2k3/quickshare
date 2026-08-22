using System.Globalization;
using System.Text;
using System.Xml.Linq;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Protocols.S3;

/// <summary>
/// An S3-compatible gateway over the workspace filesystem (issue #265).
///
/// Every backup tool, CI job and sync client already speaks S3. Offering the protocol means a
/// procurement pipeline integrates with no custom code at all — which is a different, larger
/// audience than the JSON API reaches, and the same audience the WebDAV mount (#195) was built
/// for with a different set of habits.
///
/// The bucket is the workspace and the key is the path, so this and WebDAV are two dialects for
/// one filesystem (<see cref="WorkspaceFileSystem"/>). Where S3 differs from a filesystem it is
/// followed rather than fought: there are no directories, so a key with slashes creates the
/// folders it needs, within the depth the folder model allows.
///
/// **Single-part uploads only.** Multipart is answered with <c>NotImplemented</c> rather than
/// half-supported: our storage provider's multipart is presigned client-to-bucket, which is the
/// wrong shape for parts that arrive here, and a gateway that accepted <c>CreateMultipartUpload</c>
/// and then lost parts would be worse than one that says no. rclone's default cutoff is 200 MiB;
/// the AWS CLI's is 8 MB and configurable. Both are documented.
/// </summary>
public static class S3Endpoints
{
    private const string Prefix = RoutePaths.S3Gateway;
    private static readonly XNamespace S3Ns = "http://s3.amazonaws.com/doc/2006-03-01/";

    private const int DefaultMaxKeys = 1000;
    private const string StorageClass = "STANDARD";
    private const string DelimiterParam = "delimiter";
    private const string PrefixParam = "prefix";
    private const string MaxKeysParam = "max-keys";
    private const string ContinuationParam = "continuation-token";
    private const string UploadsParam = "uploads";
    private const string UploadIdParam = "uploadId";

    private static readonly string[] Verbs =
        [HttpMethods.Get, HttpMethods.Head, HttpMethods.Put, HttpMethods.Delete, HttpMethods.Post];

    public static void MapS3Endpoints(this WebApplication app)
    {
        app.MapMethods($"{Prefix}/{{**path}}", Verbs, HandleAsync).AllowAnonymous();
        app.MapMethods(Prefix, Verbs, HandleAsync).AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        S3Credentials credentials,
        WorkspaceFileSystem fs,
        WorkspaceService workspaces,
        CancellationToken ct)
    {
        var session = await credentials.AuthenticateAsync(context, ct);
        if (session is null)
            return S3Error.AccessDenied();

        var segments = WorkspaceFileSystem.Segments(context.Request.RouteValues["path"] as string);
        var method = context.Request.Method;

        // Multipart is addressed by query parameter, so it has to be refused before the verb is
        // dispatched — otherwise ?uploads would look like an ordinary POST to a bucket.
        if (context.Request.Query.ContainsKey(UploadsParam) || context.Request.Query.ContainsKey(UploadIdParam))
            return S3Error.NotImplemented("Multipart upload is not supported by this gateway.");

        if (segments.Length == 0)
            return HttpMethods.IsGet(method) ? ListBuckets(session) : S3Error.MethodNotAllowed();

        if (!string.Equals(segments[0], BucketName(session.Workspace), StringComparison.OrdinalIgnoreCase))
            return S3Error.NoSuchBucket();

        var key = string.Join('/', segments[1..]);

        if (key.Length == 0)
        {
            return method switch
            {
                _ when HttpMethods.IsGet(method) => await ListObjectsAsync(context, fs, session, ct),
                _ when HttpMethods.IsHead(method) => Results.Ok(),
                _ => S3Error.MethodNotAllowed(),
            };
        }

        return method switch
        {
            _ when HttpMethods.IsGet(method) || HttpMethods.IsHead(method) => await GetObjectAsync(context, fs, session, key, ct),
            _ when HttpMethods.IsPut(method) => await PutObjectAsync(context, credentials, fs, workspaces, session, key, ct),
            _ when HttpMethods.IsDelete(method) => await DeleteObjectAsync(context, fs, workspaces, session, key, ct),
            _ => S3Error.MethodNotAllowed(),
        };
    }

    // ── Operations ──

    private static S3XmlResult ListBuckets(S3Session session)
    {
        // One workspace, one bucket. A key never sees another workspace, so the name only has to
        // round-trip for this one.
        var body = new XElement(S3Ns + "ListAllMyBucketsResult",
            new XElement(S3Ns + "Owner",
                new XElement(S3Ns + "ID", session.Workspace.Id.ToString("N")),
                new XElement(S3Ns + "DisplayName", session.Workspace.Name)),
            new XElement(S3Ns + "Buckets",
                new XElement(S3Ns + "Bucket",
                    new XElement(S3Ns + "Name", BucketName(session.Workspace)),
                    new XElement(S3Ns + "CreationDate", Timestamp(session.Workspace.CreatedAt)))));

        return new S3XmlResult(body, StatusCodes.Status200OK);
    }

    private static async Task<IResult> ListObjectsAsync(
        HttpContext context, WorkspaceFileSystem fs, S3Session session, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Read))
            return S3Error.AccessDenied();

        var query = context.Request.Query;
        var prefix = query[PrefixParam].ToString();
        var delimiter = query[DelimiterParam].ToString();
        var maxKeys = int.TryParse(query[MaxKeysParam], out var requested) && requested is > 0 and <= DefaultMaxKeys
            ? requested
            : DefaultMaxKeys;

        var start = int.TryParse(query[ContinuationParam], out var token) && token > 0 ? token : 0;

        var all = (await fs.ListAllAsync(session.WorkspaceId, ct))
            .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToList();

        var contents = new List<XElement>();
        var commonPrefixes = new SortedSet<string>(StringComparer.Ordinal);
        var index = start;

        for (; index < all.Count && contents.Count + commonPrefixes.Count < maxKeys; index++)
        {
            var item = all[index];
            var objectKey = item.Key;
            var entry = item.Entry;

            // With a delimiter, everything below the next separator collapses into one prefix —
            // which is how every client renders a folder it was never given.
            if (delimiter.Length > 0)
            {
                var cut = objectKey.IndexOf(delimiter, prefix.Length, StringComparison.Ordinal);
                if (cut >= 0)
                {
                    commonPrefixes.Add(objectKey[..(cut + delimiter.Length)]);
                    continue;
                }
            }

            contents.Add(new XElement(S3Ns + "Contents",
                new XElement(S3Ns + "Key", objectKey),
                new XElement(S3Ns + "LastModified", Timestamp(entry.ModifiedAt)),
                new XElement(S3Ns + "ETag", ETag(entry)),
                new XElement(S3Ns + "Size", entry.Size.ToString(CultureInfo.InvariantCulture)),
                new XElement(S3Ns + "StorageClass", StorageClass)));
        }

        var truncated = index < all.Count;

        var body = new XElement(S3Ns + "ListBucketResult",
            new XElement(S3Ns + "Name", BucketName(session.Workspace)),
            new XElement(S3Ns + "Prefix", prefix),
            new XElement(S3Ns + "KeyCount", (contents.Count + commonPrefixes.Count).ToString(CultureInfo.InvariantCulture)),
            new XElement(S3Ns + "MaxKeys", maxKeys.ToString(CultureInfo.InvariantCulture)),
            new XElement(S3Ns + "Delimiter", delimiter),
            new XElement(S3Ns + "IsTruncated", truncated ? "true" : "false"),
            // A continuation token rather than a silent cut: a listing that stops without saying
            // so reads as "that is everything", and a backup tool would believe it.
            truncated ? new XElement(S3Ns + "NextContinuationToken", index.ToString(CultureInfo.InvariantCulture)) : null,
            contents,
            commonPrefixes.Select(p => new XElement(S3Ns + "CommonPrefixes", new XElement(S3Ns + "Prefix", p))));

        return new S3XmlResult(body, StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetObjectAsync(
        HttpContext context, WorkspaceFileSystem fs, S3Session session, string key, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Read))
            return S3Error.AccessDenied();

        var node = await fs.ResolveAsync(session.WorkspaceId, WorkspaceFileSystem.Segments(key), ct);
        if (node?.File is not { } file)
            return S3Error.NoSuchKey();

        if (HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.ContentLength = file.FileSize;
            context.Response.ContentType = file.ContentType;
            context.Response.Headers.ETag = $"\"{file.Sha256 ?? file.ShareCode}\"";
            return Results.Empty;
        }

        var stream = await fs.OpenAsync(file, ct);
        if (stream is null)
            return S3Error.NoSuchKey();

        // Owner-side, like every other mount read: no download counting, no recipient gates.
        return Results.Stream(stream, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> PutObjectAsync(
        HttpContext context, S3Credentials credentials, WorkspaceFileSystem fs, WorkspaceService workspaces,
        S3Session session, string key, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Write))
            return S3Error.AccessDenied();

        var segments = WorkspaceFileSystem.Segments(key);
        var folder = await fs.EnsureFolderAsync(session.WorkspaceId, segments[..^1], session.User.Id, ct);

        if (!folder.IsSuccess(out var folderId))
            return S3Error.InvalidRequest("The key is nested deeper than this workspace allows.");

        var signed = SigV4.Parse(context.Request)!;
        var body = SignedPayload.Wrap(context.Request.Body, signed, credentials.SigningKeyFor(session.Credential, signed));

        LightResults.Result<Delivery> result;
        try
        {
            result = await fs.PutAsync(
                session.User, session.WorkspaceId, folderId, segments[^1],
                context.Request.ContentType, body, context.Request.ContentLength, ct);
        }
        catch (MountPayloadException)
        {
            // The signature covered a hash of the body and the body did not match it. The object
            // that was written is unauthenticated, so it is not kept.
            return S3Error.BadDigest();
        }

        if (!result.IsSuccess(out var delivery))
        {
            result.IsFailure(out var error);
            return S3Error.From(error?.Message);
        }

        await workspaces.AuditAsync(session.WorkspaceId, session.User.Id,
            SecurityEventType.MountObjectWritten, context, ct,
            System.Text.Json.JsonSerializer.Serialize(new { protocol = "s3", key, delivery.ShareCode }));

        context.Response.Headers.ETag = $"\"{delivery.Sha256 ?? delivery.ShareCode}\"";

        return Results.Ok();
    }

    private static async Task<IResult> DeleteObjectAsync(
        HttpContext context, WorkspaceFileSystem fs, WorkspaceService workspaces,
        S3Session session, string key, CancellationToken ct)
    {
        // Same split as the WebDAV mount: destroying is "manage", not "write".
        if (!session.Grants(ApiScopes.Manage))
            return S3Error.AccessDenied();

        var node = await fs.ResolveAsync(session.WorkspaceId, WorkspaceFileSystem.Segments(key), ct);

        // S3 says deleting something that is not there succeeded, and every client relies on it.
        if (node?.File is not { } file)
            return Results.NoContent();

        var result = await fs.DeleteAsync(file, session.User.Id, ct);
        if (!result.IsSuccess())
        {
            result.IsFailure(out var error);
            return S3Error.From(error?.Message);
        }

        await workspaces.AuditAsync(session.WorkspaceId, session.User.Id,
            SecurityEventType.MountObjectDeleted, context, ct,
            System.Text.Json.JsonSerializer.Serialize(new { protocol = "s3", key }));

        return Results.NoContent();
    }

    // ── Shapes ──

    /// <summary>
    /// The workspace's name as a bucket: lowercase, DNS-safe, stable. Two workspaces may slug to
    /// the same string and it does not matter — a credential only ever sees its own.
    /// </summary>
    public static string BucketName(Workspace workspace)
    {
        var builder = new StringBuilder(workspace.Name.Length);
        var lastWasDash = false;

        foreach (var c in workspace.Name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var name = builder.ToString().Trim('-');

        return name.Length < 3 ? "workspace" : name[..Math.Min(name.Length, 63)];
    }

    private static string ETag(MountEntry entry) => $"\"{entry.ShareCode}\"";

    private static string Timestamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

/// <summary>An S3 XML body. Clients parse these, so the namespace and the shape are the contract.</summary>
internal sealed class S3XmlResult(XElement body, int statusCode) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/xml";

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), body);
        await context.Response.WriteAsync(document.ToString(SaveOptions.DisableFormatting), context.RequestAborted);
    }
}

/// <summary>
/// The error codes S3 clients act on (issue #265).
///
/// A client retries, aborts or asks for credentials based on the <c>Code</c> element, not on the
/// HTTP status alone — returning a bare status would leave rclone guessing.
/// </summary>
internal static class S3Error
{
    private static readonly XNamespace S3Ns = "http://s3.amazonaws.com/doc/2006-03-01/";

    public static IResult AccessDenied() => Build("AccessDenied", "Access denied.", StatusCodes.Status403Forbidden);
    public static IResult NoSuchBucket() => Build("NoSuchBucket", "No such bucket.", StatusCodes.Status404NotFound);
    public static IResult NoSuchKey() => Build("NoSuchKey", "No such key.", StatusCodes.Status404NotFound);
    public static IResult MethodNotAllowed() => Build("MethodNotAllowed", "Method not allowed.", StatusCodes.Status405MethodNotAllowed);
    public static IResult NotImplemented(string message) => Build("NotImplemented", message, StatusCodes.Status501NotImplemented);
    public static IResult InvalidRequest(string message) => Build("InvalidRequest", message, StatusCodes.Status400BadRequest);
    public static IResult BadDigest() => Build("BadDigest", "The body did not match the signed content hash.", StatusCodes.Status400BadRequest);

    /// <summary>Maps a filesystem refusal onto the nearest code a client already understands.</summary>
    public static IResult From(string? code) => code switch
    {
        ErrorCodes.MonthlyQuotaExceeded => Build("QuotaExceeded", "Monthly quota exceeded.", StatusCodes.Status403Forbidden),
        ErrorCodes.FileTooLarge => Build("EntityTooLarge", "The object exceeds the plan's file size limit.", StatusCodes.Status400BadRequest),
        ErrorCodes.DeliveryLocked => Build("AccessDenied", "The file is checked out by somebody else.", StatusCodes.Status403Forbidden),
        ErrorCodes.LegalHoldActive => Build("AccessDenied", "The file is under legal hold.", StatusCodes.Status403Forbidden),
        ErrorCodes.FolderPasswordRequired => Build("AccessDenied", "This folder requires a password on every upload.", StatusCodes.Status403Forbidden),
        ErrorCodes.FolderTooDeep => Build("InvalidRequest", "The key is nested deeper than this workspace allows.", StatusCodes.Status400BadRequest),
        _ => Build("InternalError", "The request could not be completed.", StatusCodes.Status500InternalServerError),
    };

    private static S3XmlResult Build(string code, string message, int status) =>
        new S3XmlResult(new XElement(S3Ns + "Error",
            new XElement(S3Ns + "Code", code),
            new XElement(S3Ns + "Message", message)), status);
}
