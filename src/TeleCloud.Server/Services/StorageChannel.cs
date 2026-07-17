using TL;

namespace TeleCloud.Server.Services;

public sealed class StorageChannel
{
    public InputPeer Peer { get; init; } = null!;
    public long ChannelId { get; init; }
    public long ChannelAccessHash { get; init; }

    public InputChannel ToInputChannel() => new(ChannelId, ChannelAccessHash);
}
