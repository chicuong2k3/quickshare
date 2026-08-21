using Hangfire;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Flags net-terms invoices that passed their due date (issue #192).
///
/// Only invoices with terms can be late: a card payment is settled the moment it is issued, so
/// there is no window in which it could be overdue. The transition is recorded once — the
/// status filter means a second pass over the same row does nothing, which is what keeps a
/// buyer from getting the same dunning email every hour until they pay.
/// </summary>
public sealed class InvoiceOverdueJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    ILogger<InvoiceOverdueJob> logger)
{
    private const string Subject = "Your QuickShare invoice is past due";

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SweepAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var late = await db.Invoices
            .Include(i => i.User)
            .Where(i => i.Status == InvoiceStatus.Issued
                && i.PaidAt == null
                && i.DueAt != null
                && i.DueAt < now)
            .ToListAsync(ct);

        if (late.Count == 0)
            return;

        foreach (var invoice in late)
        {
            invoice.Status = InvoiceStatus.Overdue;
            invoice.UpdatedAt = now;

            db.Notifications.Add(new Notification
            {
                UserId = invoice.UserId,
                Type = NotificationType.InvoiceOverdue,
                Title = invoice.Number,
                LinkUrl = RoutePaths.AccountInvoices,
            });
        }

        await db.SaveChangesAsync(ct);

        if (!emailSender.IsConfigured)
            return;

        foreach (var invoice in late)
        {
            if (string.IsNullOrWhiteSpace(invoice.User.Email))
                continue;

            try
            {
                await emailSender.SendAsync(invoice.User.Email, invoice.User.Name, Subject, Body(invoice), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Overdue notice failed for invoice {Number}", invoice.Number);
            }
        }
    }

    private static string Body(Invoice invoice) => $"""
        <p>Invoice <strong>{invoice.Number}</strong> was due on {invoice.DueAt:yyyy-MM-dd} and is still open.</p>
        <p>Amount: {invoice.Total:N0} {invoice.Currency}{(string.IsNullOrEmpty(invoice.PoNumber) ? string.Empty : $" · PO {System.Net.WebUtility.HtmlEncode(invoice.PoNumber)}")}</p>
        <p>If payment is already on its way, please ignore this note.</p>
        """;
}
