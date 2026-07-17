using TL;

namespace TeleCloud.Server.Services;

public static class TelegramInitializer
{
    public static async Task<StorageChannel> InitializeAsync(
        Client client,
        TeleCloudOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        Helpers.Log = (lvl, str) => logger.Log((LogLevel)lvl, str);

        var myself = await client.LoginUserIfNeeded();
        logger.LogInformation("Logged in as {User} (id {Id})", myself, myself.id);

        return await ResolveChannelAsync(client, options, logger, ct);
    }

    public static async Task<StorageChannel> ResolveChannelAsync(
        Client client,
        TeleCloudOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        StorageChannel storageChannel;

        if (long.TryParse(options.StorageChannel, out var cid))
        {
            var chats = await client.Messages_GetAllChats();
            var peer = chats.chats[cid];
            var accessHash = chats.chats[cid] is Channel ch ? ch.access_hash : 0;

            storageChannel = new StorageChannel
            {
                Peer = peer,
                ChannelId = cid,
                ChannelAccessHash = accessHash
            };
        }
        else
        {
            var resolved = await client.Contacts_ResolveUsername(options.StorageChannel);
            long id = 0;
            long accessHash = 0;

            if (resolved.Chat is Channel ch)
            {
                id = ch.ID;
                accessHash = ch.access_hash;
            }
            else if (resolved.User is not null)
            {
                id = resolved.User.id;
            }

            storageChannel = new StorageChannel
            {
                Peer = resolved,
                ChannelId = id,
                ChannelAccessHash = accessHash
            };
        }

        logger.LogInformation("Storage channel resolved: id={Id}", storageChannel.ChannelId);
        return storageChannel;
    }
}
