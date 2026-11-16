using System.Text;
using Hangfire;
using QuickShare.Features.Workspaces.Report;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Weekly workspace health report (issue #300): one email per workspace to its owner,
/// summarizing seats, pooled quota and delivery volume over the last week.
///
/// Weekly is the only cadence shipped. Per-workspace scheduling (daily/monthly, extra
/// recipients) would need a settings table and a per-row cron; a fixed weekly send answers
/// the "am I flying blind" problem, and the on-demand endpoint covers every other window.
/// </summary>
public sealed class WorkspaceReportJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    ILogger<WorkspaceReportJob> logger)
{
    private const string Subject = "Your QuickShare workspace this week";
    private const int WindowDays = 7;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyAsync()
    {
        if (!emailSender.IsConfigured)
            return;

        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reports = new WorkspaceReportService(db);

        var workspaces = await db.Workspaces
            .AsNoTracking()
            .Include(w => w.Owner)
            .Select(w => new { w.Id, w.Owner.Name, w.Owner.Email })
            .ToListAsync(ct);

        foreach (var workspace in workspaces)
        {
            if (string.IsNullOrWhiteSpace(workspace.Email))
                continue;

            try
            {
                var report = await reports.BuildAsync(workspace.Id, WindowDays, ct);
                if (report is null)
                    continue;

                await emailSender.SendAsync(workspace.Email, workspace.Name, Subject, BuildBody(report), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Workspace report email failed for workspace {WorkspaceId}", workspace.Id);
            }
        }
    }

    private static string BuildBody(WorkspaceReportDto report)
    {
        var rows = new StringBuilder();
        foreach (var member in report.Members)
        {
            rows.Append("<li>");
            rows.Append(System.Net.WebUtility.HtmlEncode(member.Name));
            rows.Append(" — ");
            rows.Append(member.Deliveries.ToString(CultureInfo.InvariantCulture));
            rows.Append(" deliveries, ");
            rows.Append(Megabytes(member.BytesUploaded));
            rows.Append("</li>");
        }

        var inactive = report.InactiveMembers.Count;

        return $"""
            <h2>{System.Net.WebUtility.HtmlEncode(report.Name)}</h2>
            <p>Last {report.WindowDays} days: {report.Deliveries} deliveries, {Megabytes(report.BytesUploaded)} uploaded, {report.Downloads} downloads.</p>
            <p>Seats: {report.SeatsUsed} of {report.Seats} claimed ({report.SeatsPending} invitation(s) outstanding, {inactive} seat(s) with no activity this week).</p>
            <p>Pooled quota: {Megabytes(report.PooledUsedBytes)} of {Megabytes(report.PooledQuotaBytes)} used this month.</p>
            <ul>{rows}</ul>
            """;
    }

    private static string Megabytes(long bytes) =>
        $"{bytes / 1024d / 1024d:F1} MB";
}
