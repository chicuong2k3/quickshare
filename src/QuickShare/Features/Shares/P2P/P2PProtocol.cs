namespace QuickShare.Features.Shares.P2P;

/// <summary>
/// P2P signaling wire contract. Values are shared with the JS client
/// (wwwroot/js/p2p/signaling.js) — keep them in sync.
/// </summary>
public static class P2PProtocol
{
    // Message types.
    public const string TypeRegister = "register";
    public const string TypeOffer = "offer";
    public const string TypeAnswer = "answer";
    public const string TypeIce = "ice";

    /// <summary>
    /// Public-address exchange used to detect up front whether both peers sit
    /// behind the same NAT. Relayed like offer/answer/ice; the server does not
    /// inspect the payload.
    /// </summary>
    public const string TypeNetProbe = "netprobe";
    public const string TypeError = "error";
    public const string TypeRegistered = "registered";
    public const string TypePeerJoined = "peer-joined";
    public const string TypePeerLeft = "peer-left";
    public const string TypeFileMeta = "file-meta";
    public const string TypeWaiting = "waiting";

    // Roles.
    public const string RoleSender = "sender";
    public const string RoleReceiver = "receiver";

    // Payload property names.
    public const string PropType = "type";
    public const string PropRole = "role";
    public const string PropFileMeta = "fileMeta";
    public const string PropName = "name";
    public const string PropSize = "size";
    public const string PropMime = "mime";
    public const string PropHasPin = "hasPin";
    public const string PropPinHash = "pinHash";
    public const string PropData = "data";
    public const string PropReceiverId = "receiverId";
}
