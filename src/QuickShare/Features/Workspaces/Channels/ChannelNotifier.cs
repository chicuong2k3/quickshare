using System.Text;
using System.Text.Json;

namespace QuickShare.Features.Workspaces.Channels;

/// <summary>
/// Posts workspace events into Slack or Teams (issue #180).
///
/// One body shape for both platforms: a plain <c>{"text": …}</c>, which a Slack incoming webhook
/// and a Teams connector both accept. Per-platform cards would be two renderers to keep in step
/// for a difference a channel reader would not notice.
///
/// Every call is **best-effort and never throws**. A share must not fail because somebody's chat
/// workspace is down, and the same reasoning already governs the recipient emails sent from the
/// same handler. The trade is latency: the post happens inline with a short timeout, so a slow
/// channel adds to the request rather than to a queue. That is the existing pattern here, and a
/// background queue would be new infrastructure for a call that normally takes tens of
/// milliseconds.
/// </summary>
public sealed class ChannelNotifier(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<ChannelNotifier> logger)
{
    private static readonly TimeSpan PostTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Announces a new share. No-op when the workspace has no hook, or has it switched off.</summary>
    public Task NotifyDeliveryAsync(Guid? workspaceId, string fileName, string shareLink, DateTime? expiresAt, CancellationToken ct)
    {
        var expiry = expiresAt is { } at
            ? $" · expires {at:yyyy-MM-dd HH:mm} UTC"
            : string.Empty;

        return PostAsync(workspaceId, hook => hook.NotifyOnDelivery,
            $"New delivery: {fileName}{expiry}\n{shareLink}", ct);
    }

    /// <summary>Announces a comment, so a question asked in the product does not sit unseen.</summary>
    public Task NotifyCommentAsync(Guid? workspaceId, string fileName, string author, string body, string shareLink, CancellationToken ct)
    {
        // Trimmed: a channel post is a nudge to come and read the thread, not the thread.
        var excerpt = body.Length > 200 ? body[..200] + "…" : body;

        return PostAsync(workspaceId, hook => hook.NotifyOnComment,
            $"{author} commented on {fileName}:\n{excerpt}\n{shareLink}", ct);
    }

    private async Task PostAsync(Guid? workspaceId, Func<WorkspaceChannelHook, bool> wants, string text, CancellationToken ct)
    {
        if (workspaceId is not { } id)
            return;

        WorkspaceChannelHook? hook;
        try
        {
            hook = await db.WorkspaceChannelHooks
                .FirstOrDefaultAsync(h => h.WorkspaceId == id
                    && h.Enabled
                    && h.ConsecutiveFailures < WorkspaceChannelHook.MaxConsecutiveFailures, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Channel hook lookup failed for workspace {WorkspaceId}", id);
            return;
        }

        if (hook is null || !wants(hook))
            return;

        var payload = JsonSerializer.Serialize(new { text });

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = PostTimeout;

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(hook.WebhookUrl, content, ct);

            if (response.IsSuccessStatusCode)
            {
                hook.LastPostedAt = DateTime.UtcNow;
                hook.ConsecutiveFailures = 0;
                hook.LastError = null;
            }
            else
            {
                RecordFailure(hook, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            // Includes the timeout. A chat outage must never surface as a failed upload.
            logger.LogWarning(ex, "Channel post failed for workspace {WorkspaceId}", id);
            RecordFailure(hook, ex.Message);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The health counters are bookkeeping; losing them is not worth failing the caller.
            logger.LogWarning(ex, "Could not persist channel hook health for workspace {WorkspaceId}", id);
        }
    }

    /// <summary>
    /// A hook that fails ten times running is parked. A revoked webhook returns the same error
    /// forever, and retrying it on every upload is latency spent to learn nothing.
    /// </summary>
    private static void RecordFailure(WorkspaceChannelHook hook, string error)
    {
        hook.ConsecutiveFailures++;
        hook.LastError = error.Length > WorkspaceChannelHook.ErrorMaxLength
            ? error[..WorkspaceChannelHook.ErrorMaxLength]
            : error;
        hook.UpdatedAt = DateTime.UtcNow;
    }
}
