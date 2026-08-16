using System.Text;
using LightResults;

namespace QuickShare.Features.Deliveries.Calendar;

public record DeliveryCalendarQuery(string ShareCode);

/// <summary>
/// Builds a minimal VCALENDAR for a delivery's expiry (issue #232): one VEVENT at the
/// expiry instant plus a VALARM 24h before, so a recipient never loses a link unnoticed.
/// </summary>
public sealed class DeliveryCalendarHandler(IHttpContextAccessor http)
{
    private const string IcsTimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    public async Task<Result<string>> Handle(DeliveryCalendarQuery query, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null || delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
            return Result.Failure<string>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

        if (delivery.ExpiresAt is not { } expiresAt || expiresAt < DateTime.UtcNow)
            return Result.Failure<string>(new LightResults.Error(ErrorCodes.DeliveryNoExpiry));

        var request = http.HttpContext!.Request;
        var link = $"{request.Scheme}://{request.Host}/d/{delivery.ShareCode}";

        // ponytail: no 75-octet line folding — every mainstream client tolerates long lines;
        // add RFC 5545 folding if a client ever chokes.
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//QuickShare//Delivery Expiry//EN\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(delivery.ShareCode).Append("@quickshare\r\n");
        sb.Append("DTSTAMP:").Append(DateTime.UtcNow.ToString(IcsTimestampFormat, CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("DTSTART:").Append(expiresAt.ToString(IcsTimestampFormat, CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("SUMMARY:").Append(EscapeText($"QuickShare link expires: {delivery.FileName}")).Append("\r\n");
        sb.Append("DESCRIPTION:").Append(EscapeText(link)).Append("\r\n");
        sb.Append("URL:").Append(link).Append("\r\n");
        sb.Append("BEGIN:VALARM\r\n");
        sb.Append("ACTION:DISPLAY\r\n");
        sb.Append("DESCRIPTION:").Append(EscapeText($"QuickShare link expires soon: {delivery.FileName}")).Append("\r\n");
        sb.Append("TRIGGER:-P1D\r\n");
        sb.Append("END:VALARM\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");

        return Result.Success(sb.ToString());
    }

    /// <summary>RFC 5545 TEXT escaping: backslash, semicolon, comma, newline.</summary>
    private static string EscapeText(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
