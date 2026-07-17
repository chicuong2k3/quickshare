using TL;

namespace TeleCloud.Server.Infrastructure;

/// <summary>
/// Deletes a Telegram storage-channel message by its id. Abstracted so the cleanup
/// service can be unit-tested against a fake without a live <see cref="WTelegram.Client"/>.
/// </summary>
public interface ITempShareMessageDeleter
{
    /// <summary>Deletes the given message id from the storage channel. Returns true on success.</summary>
    Task<bool> DeleteAsync(int messageId, CancellationToken ct = default);
}

/// <summary>
/// Production deleter that forwards to <see cref="TelegramService.Client.Channels_DeleteMessages"/>.
/// </summary>
public sealed class TelegramMessageDeleter(TelegramService telegram, ILogger<TelegramMessageDeleter> logger) : ITempShareMessageDeleter
{
    public async Task<bool> DeleteAsync(int messageId, CancellationToken ct = default)
    {
        try
        {
            var channel = await telegram.GetChannelAsync(ct);
            await telegram.Client.Channels_DeleteMessages(channel.ToInputChannel(), [messageId]);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Telegram message {MessageId}", messageId);
            return false;
        }
    }
}
