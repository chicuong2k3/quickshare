using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Notifications.EmailDeliverability;

public sealed record BrevoWebhookRequest(
    string? Event,
    string? Email
);

public sealed record EmailSuppressionItem(
    Guid Id,
    string Email,
    string Reason,
    string Source,
    int BounceCount,
    DateTime CreatedAt
);

/// <summary>
/// POST /api/webhooks/email/brevo — Brevo email deliverability webhook (issue #108).
///
/// Handles <c>hard_bounce</c>, <c>soft_bounce</c>, <c>spam</c> / <c>spam_complaint</c>, and
/// <c>unsubscribed</c> events. Hard bounces and spam complaints immediately suppress the
/// address; soft bounces increment <see cref="EmailSuppression.BounceCount"/> and suppress
/// once the count reaches <see cref="EmailSender.SoftBounceThreshold"/> (3x).
/// </summary>
public sealed class BrevoEmailWebhookEndpoint : Endpoint<BrevoWebhookRequest>
{
    public const string SecretHeader = "X-QuickShare-Email-Webhook-Secret";

    public override void Configure()
    {
        Post(RoutePaths.BrevoEmailWebhook);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Brevo email bounce and complaint webhook";
            s.Description = "Issue #108. Suppresses hard bounces, spam complaints, and repeated soft bounces.";
        });
    }

    public override async Task HandleAsync(BrevoWebhookRequest req, CancellationToken ct)
    {
        var options = Resolve<IOptions<EmailOptions>>().Value;
        if (!string.IsNullOrEmpty(options.BounceWebhookSecret) && !SecretMatches(options.BounceWebhookSecret))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var email = EmailSuppression.Normalize(req.Email);
        var evt = (req.Event ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(evt))
        {
            await Send.OkAsync(new { handled = false }, ct);
            return;
        }

        var reason = evt switch
        {
            "hard_bounce" or "hardbounce" or "blocked" or "invalid_email" => EmailSuppression.ReasonHardBounce,
            "soft_bounce" or "softbounce" or "deferred" => EmailSuppression.ReasonSoftBounce,
            "spam" or "spam_complaint" or "complaint" => EmailSuppression.ReasonSpamComplaint,
            "unsubscribed" or "unsubscribe" => EmailSuppression.ReasonUnsubscribed,
            _ => null
        };

        if (reason is null)
        {
            await Send.OkAsync(new { handled = false }, ct);
            return;
        }

        var db = Resolve<AppDbContext>();
        var existing = await db.EmailSuppressions
            .FirstOrDefaultAsync(s => s.Email == email, ct);

        if (existing is null)
        {
            db.EmailSuppressions.Add(new EmailSuppression
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                Reason = reason,
                Source = EmailSuppression.SourceBrevoWebhook,
                BounceCount = 1
            });
        }
        else
        {
            existing.BounceCount += 1;
            if (reason != EmailSuppression.ReasonSoftBounce || existing.Reason == EmailSuppression.ReasonSoftBounce)
            {
                existing.Reason = reason;
            }
            existing.Source = EmailSuppression.SourceBrevoWebhook;
        }

        // Spam complaints and unsubscribes also flip the account's notification opt-in off.
        if (reason is EmailSuppression.ReasonSpamComplaint or EmailSuppression.ReasonUnsubscribed or EmailSuppression.ReasonHardBounce)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is not null)
            {
                user.EmailOnDownload = false;
            }
        }

        await db.SaveChangesAsync(ct);

        var suppressed = reason != EmailSuppression.ReasonSoftBounce
            || (existing?.BounceCount ?? 1) >= EmailSender.SoftBounceThreshold;

        await Send.OkAsync(new { handled = true, suppressed, reason }, ct);
    }

    private bool SecretMatches(string expected)
    {
        var provided = HttpContext.Request.Headers[SecretHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}

/// <summary>GET /api/admin/email-suppressions — admin view of the email suppression list (issue #108).</summary>
public sealed class ListEmailSuppressionsEndpoint : EndpointWithoutRequest<IReadOnlyList<EmailSuppressionItem>>
{
    public override void Configure()
    {
        Get(RoutePaths.AdminEmailSuppressionsApi);
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", StatusCodes.Status403Forbidden);
            return;
        }

        var db = Resolve<AppDbContext>();
        var items = await db.EmailSuppressions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new EmailSuppressionItem(s.Id, s.Email, s.Reason, s.Source, s.BounceCount, s.CreatedAt))
            .ToListAsync(ct);

        await Send.OkAsync(items, ct);
    }
}

/// <summary>DELETE /api/admin/email-suppressions/{id} — admin manual release from suppression (issue #108).</summary>
public sealed class ReleaseEmailSuppressionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/admin/email-suppressions/{id}");
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", StatusCodes.Status403Forbidden);
            return;
        }

        var id = Route<Guid>("id");
        var db = Resolve<AppDbContext>();
        var row = await db.EmailSuppressions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        db.EmailSuppressions.Remove(row);
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>POST /admin/email-suppressions/release — SSR form handler for releasing a suppressed email (issue #108).</summary>
public sealed class ReleaseEmailSuppressionFormEndpoint : EndpointWithoutRequest
{
    public const string FormRoute = "/admin/email-suppressions/release";
    public const string IdField = "suppressionId";

    public override void Configure()
    {
        Post(FormRoute);
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            await Send.ResponseAsync(null!, StatusCodes.Status403Forbidden, ct);
            return;
        }

        try
        {
            await Resolve<IAntiforgery>().ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var form = await HttpContext.Request.ReadFormAsync(ct);
        if (!Guid.TryParse(form[IdField], out var id))
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var db = Resolve<AppDbContext>();
        var row = await db.EmailSuppressions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is not null)
        {
            db.EmailSuppressions.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = RoutePaths.AdminEmailSuppressions;
    }
}
