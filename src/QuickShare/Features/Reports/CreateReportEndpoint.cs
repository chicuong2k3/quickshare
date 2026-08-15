using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Features.Reports;

/// <summary>
/// Creates an anonymous abuse/DMCA report against a delivery (issue #53).
/// Reporter identity is stored as a hashed IP only (privacy #26). The
/// endpoint is anonymous and rate-limited by the global middleware (#52).
/// </summary>
public sealed class CreateReportEndpoint : Endpoint<CreateReportRequest>
{
    private const string ShareCodeRouteKey = "ShareCode";
    private const int MaxNoteLength = 2000;

    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/report");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Reports));
        Summary(s =>
        {
            s.Summary = "Report a delivery (abuse, copyright, malware)";
            s.Description = "Anonymous. Creates a report for admin review. Stores only a hashed reporter IP.";
            s.Responses[202] = "Report accepted.";
            s.Responses[404] = "Delivery not found.";
            s.Responses[400] = "Invalid category or note too long.";
        });
    }

    public override async Task HandleAsync(CreateReportRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>(ShareCodeRouteKey)!;

        if (!IsValidCategory(req.Category))
        {
            ThrowError("Invalid category", 400);
            return;
        }

        if (req.Note is { Length: > MaxNoteLength })
        {
            ThrowError("Note too long", 400);
            return;
        }

        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries
            .AsNoTracking()
            .Select(d => new { d.Id, d.ShareCode })
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode, ct);

        if (delivery is null)
        {
            ThrowError("Delivery not found", 404);
            return;
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        db.AbuseReports.Add(new AbuseReport
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            Category = req.Category,
            Note = req.Note,
            ReporterIpHash = HashIp(ip)
        });
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new { accepted = true }, ct);
    }

    private static bool IsValidCategory(string category) =>
        category is AbuseReportCategory.Abuse
            or AbuseReportCategory.Copyright
            or AbuseReportCategory.Malware
            or AbuseReportCategory.Other;

    private static string HashIp(string ip)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexStringLower(bytes);
    }
}

public class CreateReportRequest
{
    public string Category { get; set; } = string.Empty;
    public string? Note { get; set; }
}
