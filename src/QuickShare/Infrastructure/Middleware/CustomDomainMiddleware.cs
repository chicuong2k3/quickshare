using Microsoft.Extensions.Options;
using QuickShare.Features.Workspaces.Domains;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// Keeps a workspace's white-label hostname to the pages it is for (issue #81).
///
/// A verified tenant domain exists so an agency's client sees the agency. Everything else the
/// app serves — sign-in, the account area, the admin surface, the marketing pages — has no
/// business answering there: it is not branded (deliberately, see <c>WorkspaceBrand</c>), it
/// invites somebody to type our credentials into a hostname we do not control, and it makes the
/// tenant's domain look like a second copy of the product.
///
/// So on a tenant host, recipient surfaces are served and everything else is redirected to the
/// canonical address. API and asset routes are allowed through because the recipient page is
/// built out of them, and a page that renders without its own download endpoint is not served,
/// it is broken.
/// </summary>
public sealed class CustomDomainMiddleware(RequestDelegate next, IOptions<EmailOptions> emailOptions)
{
    /// <summary>Paths a recipient legitimately reaches on a tenant host.</summary>
    private static readonly string[] AllowedPrefixes =
    [
        RoutePaths.DeliveryPage,   // /d/{code} and /d/{code}/stats
        "/brand/",                 // the workspace's own logo and fonts
        "/api/",                   // the recipient page is built out of these
        "/css/",
        "/js/",
        "/lib/",
        "/_framework/",
        "/_content/",
        "/favicon",
        "/robots.txt",
        "/manifest.webmanifest",
        "/sw.js",
    ];

    public async Task InvokeAsync(HttpContext context, WorkspaceDomainReader domains)
    {
        var host = context.Request.Host.Host;
        var workspaceId = await domains.WorkspaceForHostAsync(host, context.RequestAborted);

        if (workspaceId is null || IsAllowed(context.Request.Path))
        {
            await next(context);
            return;
        }

        // A redirect rather than a 404: somebody who typed the agency's address expecting the
        // product should land on it, not on an error that looks like the domain is broken.
        var canonical = emailOptions.Value.BaseUrl.TrimEnd('/');
        context.Response.Redirect($"{canonical}{context.Request.Path}{context.Request.QueryString}");
    }

    private static bool IsAllowed(PathString path)
    {
        var value = path.Value ?? string.Empty;

        return Array.Exists(AllowedPrefixes,
            prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
