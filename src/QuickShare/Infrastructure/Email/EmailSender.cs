using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// SMTP sender. Configured against Brevo's relay by default, but any SMTP provider works —
/// only appsettings/env change. Checks <see cref="EmailSuppression"/> before sending (issue #108)
/// so hard-bounced or complaining addresses are never contacted again.
/// </summary>
public sealed class EmailSender(
    IOptions<EmailOptions> options,
    IServiceScopeFactory? scopeFactory = null,
    ILogger<EmailSender>? logger = null)
{
    public const int SoftBounceThreshold = 3;

    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Returns true if <paramref name="email"/> is currently on the suppression list (issue #108).</summary>
    public async Task<bool> IsSuppressedAsync(string? email, CancellationToken ct = default)
    {
        var normalized = EmailSuppression.Normalize(email);
        if (string.IsNullOrEmpty(normalized) || scopeFactory is null)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
        {
            return false;
        }

        var row = await db.EmailSuppressions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Email == normalized, ct);

        if (row is null)
        {
            return false;
        }

        // Soft bounces only suppress once they reach the 3x threshold.
        if (row.Reason == EmailSuppression.ReasonSoftBounce && row.BounceCount < SoftBounceThreshold)
        {
            return false;
        }

        return true;
    }

    // ponytail: one connection per email — pool the SmtpClient if we ever send more than a few per minute.
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct)
    {
        if (await IsSuppressedAsync(toEmail, ct))
        {
            logger?.LogInformation("Skipping outbound email to suppressed address.");
            return;
        }

        if (!_options.IsConfigured)
            throw new InvalidOperationException("Email is not configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.From));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);

        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
