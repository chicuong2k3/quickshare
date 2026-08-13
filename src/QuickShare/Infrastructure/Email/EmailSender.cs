using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// SMTP sender. Configured against Brevo's relay by default, but any SMTP provider works —
/// only appsettings/env change.
/// </summary>
public sealed class EmailSender(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    // ponytail: one connection per email — pool the SmtpClient if we ever send more than a few per minute.
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct)
    {
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
