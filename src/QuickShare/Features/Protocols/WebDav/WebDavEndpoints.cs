using System.Globalization;
using System.Text;
using System.Xml.Linq;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Protocols.WebDav;

/// <summary>
/// WebDAV over the workspace filesystem (issue #195).
///
/// Written as minimal-API routes rather than FastEndpoints for the same reason SAML was: the
/// verbs are not HTTP verbs anybody models a JSON API with (PROPFIND, MKCOL), the response body
/// is XML with a multi-status code, and the caller is Finder, rclone or a twenty-year-old batch
/// job rather than a client we ship.
///
/// The point of the feature is that nobody has to learn anything: a team migrating off FTP or a
/// network share drops files into a folder and they become deliveries, with the workspace's own
/// expiry, quota and folder policy applied — the same rules the upload form obeys, because they
/// live in <see cref="WorkspaceFileSystem"/> rather than in the form.
///
/// **Class 1 only — no LOCK.** Windows Explorer and macOS Finder both refuse to *write* to a
/// server that cannot lock, so they mount read-only; rclone, davfs2 and Cyberduck read and write
/// happily. Implementing LOCK properly means lock-null resources (a lock on a path that does not
/// exist yet), and a fake LOCK that returns a token while locking nothing is worse than none —
/// it tells a client its exclusive access is real. #308 already models a checkout lock, and that
/// is the honest thing to map a future LOCK onto.
/// </summary>
public static class WebDavEndpoints
{
    private const string Prefix = RoutePaths.WebDav;
    private static readonly XNamespace Dav = "DAV:";

    private const string Propfind = "PROPFIND";
    private const string Mkcol = "MKCOL";
    private const string DepthHeader = "Depth";
    private const string DepthInfinity = "infinity";

    private static readonly string[] Verbs =
        [HttpMethods.Options, HttpMethods.Head, HttpMethods.Get, HttpMethods.Put, HttpMethods.Delete, Propfind, Mkcol];

    public static void MapWebDavEndpoints(this WebApplication app)
    {
        app.MapMethods($"{Prefix}/{{**path}}", Verbs, HandleAsync).AllowAnonymous();
        app.MapMethods(Prefix, Verbs, HandleAsync).AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        MountCredentials credentials,
        WorkspaceFileSystem fs,
        WorkspaceService workspaces,
        CancellationToken ct)
    {
        // OPTIONS is answered before authentication: a client discovers whether the server
        // speaks WebDAV at all before it has anything to send, and every one of them probes.
        if (HttpMethods.IsOptions(context.Request.Method))
            return Options();

        var session = await credentials.ResolveAsync(context, ct);
        if (session is null)
        {
            MountCredentials.Challenge(context);
            return Results.Empty;
        }

        var path = context.Request.RouteValues["path"] as string;
        var segments = WorkspaceFileSystem.Segments(path);
        var method = context.Request.Method;

        // PUT and MKCOL address something that does not exist yet, so they resolve their parent
        // rather than themselves.
        if (HttpMethods.IsPut(method))
            return await PutAsync(context, fs, workspaces, session, segments, ct);

        if (string.Equals(method, Mkcol, StringComparison.OrdinalIgnoreCase))
            return await MkcolAsync(context, fs, workspaces, session, segments, ct);

        var node = await fs.ResolveAsync(session.WorkspaceId, segments, ct);
        if (node is null)
            return Results.NotFound();

        if (string.Equals(method, Propfind, StringComparison.OrdinalIgnoreCase))
            return await PropfindAsync(context, fs, session, node, segments, ct);

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            return await GetAsync(context, fs, session, node, ct);

        if (HttpMethods.IsDelete(method))
            return await DeleteAsync(context, fs, workspaces, session, node, ct);

        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    // ── Verbs ──

    private static IResult Options() => Results.Extensions.Dav();

    private static async Task<IResult> PropfindAsync(
        HttpContext context, WorkspaceFileSystem fs, MountSession session, MountNode node, string[] segments, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Read))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var depth = context.Request.Headers[DepthHeader].ToString();

        // Refused rather than walked: an infinite PROPFIND on a workspace with two hundred
        // folders is a denial-of-service a single client can trigger by accident, and RFC 4918
        // reserves this exact refusal for servers that decline it.
        if (string.Equals(depth, DepthInfinity, StringComparison.OrdinalIgnoreCase))
            return Results.Extensions.FiniteDepth();

        var responses = new List<XElement> { Describe(Href(segments, node.IsCollection), node) };

        if (node.IsCollection && depth != "0")
        {
            foreach (var entry in await fs.ListAsync(session.WorkspaceId, node.FolderId, ct))
                responses.Add(Describe(Href([.. segments, entry.Name], entry.IsCollection), entry));
        }

        return Results.Extensions.MultiStatus(new XElement(Dav + "multistatus", responses));
    }

    private static async Task<IResult> GetAsync(
        HttpContext context, WorkspaceFileSystem fs, MountSession session, MountNode node, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Read))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // A collection has no bytes. Some clients ask anyway; 405 is what tells them to PROPFIND.
        if (node.IsCollection || node.File is not { } file)
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

        if (HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.ContentLength = file.FileSize;
            context.Response.ContentType = file.ContentType;
            return Results.Empty;
        }

        var stream = await fs.OpenAsync(file, ct);
        if (stream is null)
            return Results.NotFound();

        // No download counting, no recipient gates: this is the team reading its own file, and
        // charging it to the recipient statistics would corrupt the only number the sender asked
        // for. Range support comes from the framework's file-result handling.
        return Results.Stream(stream, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> PutAsync(
        HttpContext context, WorkspaceFileSystem fs, WorkspaceService workspaces, MountSession session, string[] segments, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Write))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (segments.Length == 0)
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

        var parent = await fs.ResolveAsync(session.WorkspaceId, segments[..^1], ct);
        if (parent is null || !parent.IsCollection)
            return Results.Conflict();

        var name = segments[^1];
        var result = await fs.PutAsync(
            session.User, session.WorkspaceId, parent.FolderId, name,
            context.Request.ContentType, context.Request.Body, context.Request.ContentLength, ct);

        if (!result.IsSuccess(out var delivery))
        {
            result.IsFailure(out var error);
            return Failure(error?.Message);
        }

        await workspaces.AuditAsync(session.WorkspaceId, session.User.Id,
            SecurityEventType.MountObjectWritten, context, ct,
            System.Text.Json.JsonSerializer.Serialize(new { protocol = "webdav", name, delivery.ShareCode }));

        return Results.StatusCode(StatusCodes.Status201Created);
    }

    private static async Task<IResult> MkcolAsync(
        HttpContext context, WorkspaceFileSystem fs, WorkspaceService workspaces, MountSession session, string[] segments, CancellationToken ct)
    {
        if (!session.Grants(ApiScopes.Write))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (segments.Length == 0)
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

        // A body on MKCOL means extended-mkcol, which this does not implement. RFC 4918 says to
        // refuse it rather than ignore it — silently dropping requested properties would tell
        // the client its collection was created the way it asked.
        if (context.Request.ContentLength > 0)
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        var parent = await fs.ResolveAsync(session.WorkspaceId, segments[..^1], ct);
        if (parent is null || !parent.IsCollection)
            return Results.Conflict();

        var result = await fs.CreateFolderAsync(session.WorkspaceId, parent.FolderId, segments[^1], session.User.Id, ct);

        if (!result.IsSuccess(out var folder))
        {
            result.IsFailure(out var error);
            return Failure(error?.Message);
        }

        await workspaces.AuditAsync(session.WorkspaceId, session.User.Id,
            SecurityEventType.MountFolderCreated, context, ct,
            System.Text.Json.JsonSerializer.Serialize(new { protocol = "webdav", folder.Name }));

        return Results.StatusCode(StatusCodes.Status201Created);
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context, WorkspaceFileSystem fs, WorkspaceService workspaces, MountSession session, MountNode node, CancellationToken ct)
    {
        // Destroying things is the "manage" scope, not "write": a key that a nightly job uses to
        // drop a report should not be able to empty the drive.
        if (!session.Grants(ApiScopes.Manage))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Deleting a collection would cascade over deliveries the client never listed. Folder
        // deletion stays where it can say "this folder is not empty" — the API and the UI.
        if (node.IsCollection || node.File is not { } file)
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

        var result = await fs.DeleteAsync(file, session.User.Id, ct);

        if (!result.IsSuccess())
        {
            result.IsFailure(out var error);
            return Failure(error?.Message);
        }

        await workspaces.AuditAsync(session.WorkspaceId, session.User.Id,
            SecurityEventType.MountObjectDeleted, context, ct,
            System.Text.Json.JsonSerializer.Serialize(new { protocol = "webdav", file.ShareCode }));

        return Results.NoContent();
    }

    // ── XML ──

    private static XElement Describe(string href, MountNode node) =>
        node.IsCollection
            ? Response(href, true, 0, node.Folder?.UpdatedAt ?? node.Folder?.CreatedAt ?? DateTime.UtcNow, null, Name(node))
            : Response(href, false, node.File!.FileSize, node.File.UpdatedAt ?? node.File.CreatedAt, node.File.ContentType, Name(node));

    private static XElement Describe(string href, MountEntry entry) =>
        Response(href, entry.IsCollection, entry.Size, entry.ModifiedAt, entry.IsCollection ? null : entry.ContentType, entry.Name);

    private static string Name(MountNode node) =>
        node.Folder?.Name ?? node.File?.FileName ?? string.Empty;

    private static XElement Response(
        string href, bool isCollection, long size, DateTime modified, string? contentType, string displayName)
    {
        var props = new List<XElement>
        {
            new(Dav + "displayname", displayName),
            new(Dav + "resourcetype", isCollection ? new XElement(Dav + "collection") : null),
            new(Dav + "creationdate", modified.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
            new(Dav + "getlastmodified", modified.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture)),
        };

        if (!isCollection)
        {
            props.Add(new XElement(Dav + "getcontentlength", size.ToString(CultureInfo.InvariantCulture)));
            props.Add(new XElement(Dav + "getcontenttype", contentType ?? WorkspaceFileSystem.DefaultContentType));
        }

        return new XElement(Dav + "response",
            new XElement(Dav + "href", href),
            new XElement(Dav + "propstat",
                new XElement(Dav + "prop", props),
                new XElement(Dav + "status", "HTTP/1.1 200 OK")));
    }

    /// <summary>Each segment escaped on its own, so a slash in a name can never split a path.</summary>
    private static string Href(string[] segments, bool isCollection)
    {
        var builder = new StringBuilder(Prefix);

        foreach (var segment in segments)
            builder.Append('/').Append(Uri.EscapeDataString(segment));

        if (isCollection)
            builder.Append('/');

        return builder.ToString();
    }

    /// <summary>
    /// Maps a filesystem refusal to the status a client can act on. 423 for a checkout lock is
    /// the one WebDAV itself defines, and it is the same condition #308 already refuses replace
    /// with — the drive and the web UI say the same thing about the same file.
    /// </summary>
    private static IResult Failure(string? code) => code switch
    {
        ErrorCodes.DeliveryLocked => Results.StatusCode(StatusCodes.Status423Locked),
        ErrorCodes.LegalHoldActive => Results.StatusCode(StatusCodes.Status423Locked),
        ErrorCodes.MonthlyQuotaExceeded => Results.StatusCode(StatusCodes.Status507InsufficientStorage),
        ErrorCodes.FileTooLarge => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
        ErrorCodes.FolderNotFound => Results.NotFound(),
        ErrorCodes.FolderNameTaken => Results.StatusCode(StatusCodes.Status405MethodNotAllowed),
        // StatusCode, never Results.Forbid(): that runs the cookie scheme's forbid handler and
        // redirects a filesystem client to a sign-in page it will never render.
        ErrorCodes.FolderTooDeep => Results.StatusCode(StatusCodes.Status403Forbidden),
        ErrorCodes.FolderPasswordRequired => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}

/// <summary>The three responses WebDAV needs that <c>Results</c> has no shape for.</summary>
internal static class DavResults
{
    private static readonly XNamespace DavNs = "DAV:";
    private const string XmlContentType = "application/xml; charset=\"utf-8\"";

    public static IResult MultiStatus(this IResultExtensions _, XElement multistatus) =>
        new XmlResult(multistatus, StatusCodes.Status207MultiStatus);

    public static IResult FiniteDepth(this IResultExtensions _) =>
        new XmlResult(new XElement(DavNs + "error", new XElement(DavNs + "propfind-finite-depth")),
            StatusCodes.Status403Forbidden);

    /// <summary>
    /// The capability advertisement. <c>DAV: 1</c> and no 2 is the honest answer while LOCK is
    /// unimplemented; <c>MS-Author-Via</c> is what stops the Windows client from trying to talk
    /// FrontPage to us before it tries WebDAV.
    /// </summary>
    public static IResult Dav(this IResultExtensions _) => new DavOptionsResult();

    private sealed class XmlResult(XElement body, int statusCode) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = XmlContentType;

            var document = new XDocument(new XDeclaration("1.0", "utf-8", null), body);
            await context.Response.WriteAsync(document.ToString(SaveOptions.DisableFormatting), context.RequestAborted);
        }
    }

    private sealed class DavOptionsResult : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers["DAV"] = "1";
            context.Response.Headers["MS-Author-Via"] = "DAV";
            context.Response.Headers.Allow = "OPTIONS, PROPFIND, GET, HEAD, PUT, DELETE, MKCOL";
            context.Response.ContentLength = 0;

            return Task.CompletedTask;
        }
    }
}
