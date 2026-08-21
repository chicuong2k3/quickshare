using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QuickShare.Features.Workspaces.Streaming;

/// <summary>One audit event, in the shape a SIEM expects to receive it (issue #191).</summary>
public sealed record SiemEvent(
    Guid Id,
    DateTime Timestamp,
    string Action,
    Guid ActorId,
    string ActorEmail,
    Guid WorkspaceId,
    string IpHash,
    string UserAgent,
    string? Metadata);

/// <summary>
/// Renders audit events into the three wire formats worth supporting (issue #191).
///
/// One schema, three envelopes. The fields are identical in all of them — who, what, when, from
/// where, in which tenant — because a customer who switches from Splunk to Elastic should not
/// discover that half their saved searches were keyed to a field we only emit in one format.
///
/// The IP is shipped hashed, exactly as it is stored. A SIEM export is not a reason to start
/// keeping raw addresses we deliberately never kept (privacy #26); a receiving SOC can still
/// correlate on the hash because it is stable per address.
/// </summary>
public static class SiemEventFormatter
{
    /// <summary>CEF header fields. Version 0 is the only one anybody implements.</summary>
    private const string CefPrefix = "CEF:0|QuickShare|QuickShareCloud|1.0|";
    private const int CefSeverity = 3;

    public static string ContentType(string format) => format switch
    {
        SiemFormat.Cef => "text/plain; charset=utf-8",
        _ => "application/json; charset=utf-8",
    };

    public static string Render(string format, IReadOnlyList<SiemEvent> events) => format switch
    {
        SiemFormat.Cef => RenderCef(events),
        SiemFormat.SplunkHec => RenderSplunk(events),
        _ => RenderJson(events),
    };

    private static string RenderJson(IReadOnlyList<SiemEvent> events) =>
        JsonSerializer.Serialize(new { events });

    /// <summary>
    /// Splunk HEC takes newline-delimited objects, each wrapping the payload in <c>event</c>.
    /// Not a JSON array — HEC rejects one, which is the kind of detail that only shows up in
    /// production if it is not encoded here.
    /// </summary>
    private static string RenderSplunk(IReadOnlyList<SiemEvent> events)
    {
        var builder = new StringBuilder();

        foreach (var item in events)
        {
            builder.Append(JsonSerializer.Serialize(new
            {
                time = new DateTimeOffset(item.Timestamp, TimeSpan.Zero).ToUnixTimeSeconds(),
                sourcetype = "quickshare:audit",
                @event = item,
            }));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string RenderCef(IReadOnlyList<SiemEvent> events)
    {
        var builder = new StringBuilder();

        foreach (var item in events)
        {
            builder.Append(CefPrefix);
            builder.Append(EscapeHeader(item.Action));
            builder.Append('|');
            builder.Append(EscapeHeader(item.Action));
            builder.Append('|');
            builder.Append(CefSeverity.ToString(CultureInfo.InvariantCulture));
            builder.Append("|rt=");
            builder.Append(item.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(" suid=");
            builder.Append(EscapeValue(item.ActorId.ToString()));
            builder.Append(" suser=");
            builder.Append(EscapeValue(item.ActorEmail));
            builder.Append(" cs1Label=workspaceId cs1=");
            builder.Append(EscapeValue(item.WorkspaceId.ToString()));
            builder.Append(" cs2Label=ipHash cs2=");
            builder.Append(EscapeValue(item.IpHash));
            builder.Append(" cs3Label=userAgent cs3=");
            builder.Append(EscapeValue(item.UserAgent));
            builder.Append(" cs4Label=metadata cs4=");
            builder.Append(EscapeValue(item.Metadata ?? string.Empty));
            builder.Append(" externalId=");
            builder.Append(EscapeValue(item.Id.ToString()));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>CEF header fields are pipe-delimited, so a literal pipe has to be escaped.</summary>
    private static string EscapeHeader(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>
    /// CEF extension values are space-delimited key=value pairs: an unescaped space or equals
    /// sign silently swallows the following field, and a newline ends the event early.
    /// </summary>
    private static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("=", "\\=", StringComparison.Ordinal)
             .Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal);
}
