using System.Security.Cryptography;
using System.Text;
using LightResults;

namespace QuickShare.Features.Deliveries.Export;

public record ExportDeliveriesQuery(int Days);

/// <summary>
/// CSV export of a sender's download activity (issue #74). Pro/Studio only —
/// download tracking is the Pro-scoped surface. IPs are SHA-256-hashed and
/// truncated so the export is a reporting tool, not a deanonymization vector.
/// </summary>
public sealed class ExportDeliveriesHandler
{
    private const int DefaultDays = 30;
    private const int MaxDays = 365;
    private const int IpHashChars = 12;

    public async Task<Result<string>> Handle(ExportDeliveriesQuery query, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure<string>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var days = query.Days <= 0 ? DefaultDays : Math.Min(query.Days, MaxDays);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var rows = await (
            from t in db.DownloadTrackings
            join d in db.Deliveries on t.DeliveryId equals d.Id
            where d.SenderId == user.Id && t.DownloadedAt >= cutoff
            orderby t.DownloadedAt descending
            select new { d.ShareCode, d.FileName, t.DownloadedAt, t.IpAddress }
        ).AsNoTracking().ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("ShareCode,FileName,DownloadedAt,IpHash");
        foreach (var row in rows)
        {
            csv.Append(Csv(row.ShareCode));
            csv.Append(',');
            csv.Append(Csv(row.FileName));
            csv.Append(',');
            csv.Append(Csv(row.DownloadedAt.ToString("o")));
            csv.Append(',');
            csv.Append(HashIp(row.IpAddress));
            csv.AppendLine();
        }

        return Result.Success(csv.ToString());
    }

    private static string Csv(string? value)
    {
        var v = value ?? string.Empty;
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }

    private static string HashIp(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return string.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(hash)[..IpHashChars].ToLowerInvariant();
    }
}
