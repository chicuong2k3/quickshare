namespace QuickShare.Data.Entities;

public static class RecurrenceFrequency
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly string[] All = [Daily, Weekly, Monthly];
}

/// <summary>
/// A standing instruction to re-send a delivery on a cadence (issue #176).
///
/// Scheduled send (#69) is one-shot: at time T, go live, done. This is the other ask — "Monday's
/// report to the same five people, every week". Each cycle produces a **new delivery with a
/// fresh link**, which is the point: a link that never rotates is a link that leaks, and the
/// recipient list for last week's numbers should not open this week's.
///
/// The source delivery is the content. Replacing its file (#72) changes what the next cycle
/// sends, so "update the report" is an operation the sender already knows how to do.
/// </summary>
public record RecurringDelivery : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int RecipientsMaxLength = 1000;
    public const int ErrorMaxLength = 400;
    /// <summary>Hour of day, UTC. Minute granularity would imply a precision the hourly tick cannot keep.</summary>
    public const int MinHour = 0;
    public const int MaxHour = 23;

    public Guid OwnerId { get; set; }
    /// <summary>Set when the source delivery belongs to a workspace, so the schedule is auditable there.</summary>
    public Guid? WorkspaceId { get; set; }
    /// <summary>The delivery whose bytes each cycle copies. Deleting it stops the schedule.</summary>
    public Guid SourceDeliveryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Frequency { get; set; } = RecurrenceFrequency.Weekly;
    public int HourUtc { get; set; }
    /// <summary>Weekly only. Ignored for the other frequencies.</summary>
    public DayOfWeek DayOfWeek { get; set; } = System.DayOfWeek.Monday;
    /// <summary>Monthly only, 1–28. Capped so a schedule never silently skips February.</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>Comma-separated recipient addresses, normalised on save.</summary>
    public string Recipients { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public Guid? LastDeliveryId { get; set; }
    public int RunCount { get; set; }
    public string? LastError { get; set; }

    public User Owner { get; init; } = null!;

    public bool IsDue(DateTime now) => Enabled && NextRunAt <= now;

    /// <summary>
    /// The next occurrence strictly after <paramref name="from"/>.
    ///
    /// Always computed forward from *now*, never by adding one interval to the missed slot. A
    /// service that was down for a week must send one report on restart, not seven — catch-up
    /// storms are how a recurring feature turns into an incident.
    /// </summary>
    public DateTime ComputeNextRun(DateTime from)
    {
        var candidate = new DateTime(from.Year, from.Month, from.Day, HourUtc, 0, 0, DateTimeKind.Utc);

        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                while (candidate <= from)
                    candidate = candidate.AddDays(1);
                return candidate;

            case RecurrenceFrequency.Monthly:
                candidate = new DateTime(from.Year, from.Month, Math.Clamp(DayOfMonth, 1, 28), HourUtc, 0, 0, DateTimeKind.Utc);
                while (candidate <= from)
                    candidate = candidate.AddMonths(1);
                return candidate;

            default:
                while (candidate <= from || candidate.DayOfWeek != DayOfWeek)
                    candidate = candidate.AddDays(1);
                return candidate;
        }
    }
}
