using System.Security.Cryptography;
using QuickShare.Extensions;

namespace QuickShare.Features.Shares.P2P;

public sealed record CreateP2PShareRequest(string FileName, long FileSize, string MimeType, bool? HasPin, string BaseUrl);

public sealed record CreateP2PShareResponse(string ShareCode, string ShareLink);

public sealed class CreateP2PShareEndpoint : Endpoint<CreateP2PShareRequest, CreateP2PShareResponse>
{
    public override void Configure()
    {
        Post("/api/shares/p2p");
        AllowAnonymous();
        Validator<CreateP2PShareValidator>();
        Description(b => b.WithTags(ApiTags.P2p));
        Summary(s =>
        {
            s.Summary = "Create a P2P share link (metadata only)";
            s.Description = "Creates a share link for WebRTC P2P file transfer. No file upload required. Unlimited for all users.";
            s.Responses[201] = "Share created. Returns shareCode and shareLink.";
        });
    }

    public override async Task HandleAsync(CreateP2PShareRequest req, CancellationToken ct)
    {
        // Resolve identity: authenticated user or anonymous (IP-tracked).
        var db = Resolve<AppDbContext>();
        var principal = HttpContext.User;
        var isAuthenticated = principal.Identity?.IsAuthenticated == true;

        User? user = null;
        if (isAuthenticated)
        {
            try
            {
                user = await principal.GetUserAsync(db, ct);
            }
            catch (InvalidOperationException)
            {
                // Unknown user claims — treat as anonymous rather than failing the share.
                isAuthenticated = false;
            }
        }

        var shareCode = GenerateShareCode();

        // Ensure uniqueness
        while (await db.Deliveries.AnyAsync(d => d.ShareCode == shareCode, ct))
        {
            shareCode = GenerateShareCode();
        }

        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            ShareCode = shareCode,
            FileName = req.FileName,
            ContentType = req.MimeType,
            FileSize = req.FileSize,
            TotalBytes = req.FileSize,
            MaxDownloads = 0,
            DownloadCount = 0,
            EgressBytes = 0,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            SenderId = isAuthenticated ? user!.Id : null
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var shareLink = $"{req.BaseUrl.TrimEnd('/')}/p2p/d/{shareCode}";

        HttpContext.Response.Headers.Location = $"/p2p/d/{shareCode}";
        await Send.OkAsync(new CreateP2PShareResponse(shareCode, shareLink), ct);
    }

    private static string GenerateShareCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }
}
