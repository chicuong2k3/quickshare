using Serilog.Core;
using Serilog.Events;

namespace QuickShare.Infrastructure.Telemetry;

public sealed class PiiRedactingEnricher : ILogEventEnricher
{
    private const string RedactedValue = "[redacted]";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Email",
        "SenderEmail",
        "FileName",
        "IpAddress",
        "RemoteIp",
        "Ip",
        "AccessKeyId",
        "AccessKeySecret",
    };

    private static readonly ScalarValue Redacted = new(RedactedValue);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var key in SensitiveKeys)
        {
            if (logEvent.Properties.ContainsKey(key))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, Redacted));
            }
        }
    }
}
