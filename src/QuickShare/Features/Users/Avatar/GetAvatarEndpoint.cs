using System.Security.Claims;
using QuickShare.Extensions;

namespace QuickShare.Features.Users.Avatar;

public sealed class GetAvatarEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/me/avatar");
        Description(b => b.WithTags("Users"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();

        string html;
        try
        {
            var user = await HttpContext.User.GetUserAsync(db, ct);
            var avatarUrl = user.AvatarUrl;

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                var escapedAlt = System.Net.WebUtility.HtmlEncode(user.Name ?? "");
                html = $"<img class=\"db-avatar db-avatar--sm db-dropdown__trigger layout-avatar-btn layout-avatar-img\" src=\"{avatarUrl}\" alt=\"{escapedAlt}\" />";
            }
            else
            {
                html = $"<span class=\"db-avatar db-avatar--sm db-dropdown__trigger layout-avatar-btn\">{GetInitials(user.Name)}</span>";
            }
        }
        catch
        {
            html = $"<span class=\"db-avatar db-avatar--sm db-dropdown__trigger layout-avatar-btn\">?</span>";
        }

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }

    private static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return parts[0][..1].ToUpperInvariant();
        }
        return string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant();
    }
}
