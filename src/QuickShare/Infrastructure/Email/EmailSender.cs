using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using QuickShare.Features.Workspaces.Branding;

namespace QuickShare.Infrastructure.Email;

/// <summary>
/// Per-send sender identity and template branding (issue #124).
///
/// The envelope <c>From</c> address always remains the platform's SPF/DKIM-verified relay address
/// (<see cref="EmailOptions.From"/>), while the display name and <c>Reply-To</c> header reflect the
/// workspace's brand so recipients see "Sent by Studio" in their inbox without breaking DMARC.
/// </summary>
public sealed record EmailBrandIdentity(
    string? SenderName = null,
    string? ReplyTo = null,
    string? AccentColor = null,
    string? AccentTextColor = null,
    string? HeaderText = null,
    string? FooterText = null,
    string? LogoUrl = null,
    bool HideQuickShareBranding = false)
{
    public static EmailBrandIdentity? FromBrand(BrandView? brand, string baseUrl)
    {
        if (brand is null)
        {
            return null;
        }

        var origin = baseUrl.TrimEnd('/');
        var logoUrl = brand.HasLogo
            ? $"{origin}{BrandReader.AssetUrl(brand.WorkspaceId, BrandAssetKind.Logo)}"
            : null;

        return new EmailBrandIdentity(
            brand.EmailSenderName ?? brand.HeaderText,
            brand.EmailReplyTo,
            brand.AccentColor,
            brand.AccentTextColor,
            brand.HeaderText,
            brand.FooterText,
            logoUrl,
            brand.HideQuickShareBranding);
    }

    public static string WrapBody(string innerHtml, EmailBrandIdentity? brand)
    {
        if (brand is null)
        {
            return innerHtml;
        }

        var accent = !string.IsNullOrWhiteSpace(brand.AccentColor) ? brand.AccentColor! : "#2563eb";
        var sb = new StringBuilder();
        sb.Append("<div class=\"qs-email-brand\" style=\"font-family:system-ui,sans-serif;font-size:15px;color:#1f2937;border-top:4px solid ")
          .Append(WebUtility.HtmlEncode(accent))
          .Append(";padding-top:16px\">");

        if (!string.IsNullOrWhiteSpace(brand.LogoUrl) || !string.IsNullOrWhiteSpace(brand.HeaderText))
        {
            sb.Append("<div class=\"qs-email-brand__header\" style=\"margin-bottom:16px\">");
            if (!string.IsNullOrWhiteSpace(brand.LogoUrl))
            {
                sb.Append("<img src=\"")
                  .Append(WebUtility.HtmlEncode(brand.LogoUrl))
                  .Append("\" alt=\"")
                  .Append(WebUtility.HtmlEncode(brand.HeaderText ?? brand.SenderName ?? string.Empty))
                  .Append("\" style=\"max-height:40px;display:block;margin-bottom:8px\" />");
            }
            if (!string.IsNullOrWhiteSpace(brand.HeaderText))
            {
                sb.Append("<p style=\"margin:0;font-weight:600;color:")
                  .Append(WebUtility.HtmlEncode(accent))
                  .Append("\">")
                  .Append(WebUtility.HtmlEncode(brand.HeaderText))
                  .Append("</p>");
            }
            sb.Append("</div>");
        }

        sb.Append("<div class=\"qs-email-brand__body\">")
          .Append(innerHtml)
          .Append("</div>");

        if (!string.IsNullOrWhiteSpace(brand.FooterText) || !brand.HideQuickShareBranding)
        {
            sb.Append("<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:24px 0\" />");
            sb.Append("<div class=\"qs-email-brand__footer\" style=\"font-size:12px;color:#6b7280\">");
            if (!string.IsNullOrWhiteSpace(brand.FooterText))
            {
                sb.Append("<p style=\"margin:0 0 4px 0\">")
                  .Append(WebUtility.HtmlEncode(brand.FooterText))
                  .Append("</p>");
            }
            if (!brand.HideQuickShareBranding)
            {
                sb.Append("<p style=\"margin:0\">Powered by QuickShareCloud</p>");
            }
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }
}

/// <summary>
/// SMTP sender. Configured against Brevo's relay by default, but any SMTP provider works —
/// only appsettings/env change. Checks <see cref="EmailSuppression"/> before sending (issue #108)
/// so hard-bounced or complaining addresses are never contacted again, and supports branded sender
/// display names + Reply-To headers per send (issue #124).
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

    /// <summary>Builds the <see cref="MimeMessage"/> with optional workspace brand identity (issue #124).</summary>
    public MimeMessage BuildMessage(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        EmailBrandIdentity? brand = null)
    {
        var fromName = !string.IsNullOrWhiteSpace(brand?.SenderName)
            ? brand!.SenderName!
            : _options.FromName;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, _options.From));
        message.To.Add(new MailboxAddress(toName, toEmail));

        if (!string.IsNullOrWhiteSpace(brand?.ReplyTo) && MailboxAddress.TryParse(brand!.ReplyTo, out var replyToAddr))
        {
            message.ReplyTo.Add(replyToAddr);
        }

        message.Subject = subject;
        var wrappedBody = EmailBrandIdentity.WrapBody(htmlBody, brand);
        message.Body = new BodyBuilder { HtmlBody = wrappedBody }.ToMessageBody();
        return message;
    }

    // ponytail: one connection per email — pool the SmtpClient if we ever send more than a few per minute.
    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct) =>
        SendAsync(toEmail, toName, subject, htmlBody, brand: null, ct);

    public async Task SendAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        EmailBrandIdentity? brand,
        CancellationToken ct)
    {
        if (await IsSuppressedAsync(toEmail, ct))
        {
            logger?.LogInformation("Skipping outbound email to suppressed address.");
            return;
        }

        if (!_options.IsConfigured)
            throw new InvalidOperationException("Email is not configured.");

        var message = BuildMessage(toEmail, toName, subject, htmlBody, brand);

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);

        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
