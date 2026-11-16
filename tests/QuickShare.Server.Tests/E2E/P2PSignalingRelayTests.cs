using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using QuickShare.Features.Shares.P2P;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The P2P signaling middleware relays WebRTC messages between one sender and
/// N receivers. These tests cover the wire contract directly: registration,
/// per-receiver addressing, and the <c>netprobe</c> public-address exchange that
/// lets peers detect a cross-network pair before ICE times out.
/// </summary>
public sealed class P2PSignalingRelayTests : IClassFixture<TestWebAppFactory>
{
    private const string WsPath = "ws://localhost/ws/connect/";
    private const string SenderAddress = "203.0.113.7";
    private const string ReceiverAddress = "198.51.100.4";
    private const string AddressProperty = "address";
    private const int ReceiveTimeoutMs = 10_000;
    private const int ReceiveBufferSize = 8 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TestWebAppFactory _factory;

    public P2PSignalingRelayTests(TestWebAppFactory factory) => _factory = factory;

    private Task<WebSocket> ConnectAsync(string shareCode)
        => _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(WsPath + shareCode), CancellationToken.None);

    private static async Task SendAsync(WebSocket ws, object message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOpts));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket ws)
    {
        using var cts = new CancellationTokenSource(ReceiveTimeoutMs);
        var buffer = new byte[ReceiveBufferSize];
        var result = await ws.ReceiveAsync(buffer, cts.Token);
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Reads until a message of the wanted type arrives, so unrelated
    /// traffic (file-meta, waiting) cannot make an assertion flaky.</summary>
    private static async Task<JsonElement> ReceiveOfTypeAsync(WebSocket ws, string type)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var message = await ReceiveAsync(ws);
            if (message.GetProperty(P2PProtocol.PropType).GetString() == type)
                return message;
        }

        throw new InvalidOperationException($"No '{type}' message arrived.");
    }

    private static Task RegisterSenderAsync(WebSocket ws) => SendAsync(ws, new
    {
        type = P2PProtocol.TypeRegister,
        role = P2PProtocol.RoleSender,
        fileMeta = new { name = "probe.bin", size = 1024, mime = "application/octet-stream" },
    });

    private static Task RegisterReceiverAsync(WebSocket ws) => SendAsync(ws, new
    {
        type = P2PProtocol.TypeRegister,
        role = P2PProtocol.RoleReceiver,
    });

    [Fact]
    public async Task NetProbe_FromReceiver_ReachesSenderTaggedWithReceiverId()
    {
        var shareCode = Guid.NewGuid().ToString("n");
        using var sender = await ConnectAsync(shareCode);
        await RegisterSenderAsync(sender);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypeRegistered);

        using var receiver = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(receiver);
        var registered = await ReceiveOfTypeAsync(receiver, P2PProtocol.TypeRegistered);
        var receiverId = registered.GetProperty(P2PProtocol.PropReceiverId).GetString();

        var joined = await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);
        Assert.Equal(receiverId, joined.GetProperty(P2PProtocol.PropReceiverId).GetString());

        await SendAsync(receiver, new
        {
            type = P2PProtocol.TypeNetProbe,
            data = new { address = ReceiverAddress },
        });

        var relayed = await ReceiveOfTypeAsync(sender, P2PProtocol.TypeNetProbe);
        // Without the receiver id the sender cannot tell which of N receivers
        // the address belongs to, so a multi-receiver share would mis-attribute it.
        Assert.Equal(receiverId, relayed.GetProperty(P2PProtocol.PropReceiverId).GetString());
        Assert.Equal(ReceiverAddress, relayed.GetProperty(P2PProtocol.PropData).GetProperty(AddressProperty).GetString());
    }

    [Fact]
    public async Task NetProbe_FromSender_ReachesTheAddressedReceiverOnly()
    {
        var shareCode = Guid.NewGuid().ToString("n");
        using var sender = await ConnectAsync(shareCode);
        await RegisterSenderAsync(sender);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypeRegistered);

        using var first = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(first);
        var firstId = (await ReceiveOfTypeAsync(first, P2PProtocol.TypeRegistered))
            .GetProperty(P2PProtocol.PropReceiverId).GetString();
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);

        using var second = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(second);
        await ReceiveOfTypeAsync(second, P2PProtocol.TypeRegistered);
        await ReceiveOfTypeAsync(second, P2PProtocol.TypeFileMeta);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);

        await SendAsync(sender, new
        {
            type = P2PProtocol.TypeNetProbe,
            data = new { address = SenderAddress },
            receiverId = firstId,
        });

        var relayed = await ReceiveOfTypeAsync(first, P2PProtocol.TypeNetProbe);
        Assert.Equal(SenderAddress, relayed.GetProperty(P2PProtocol.PropData).GetProperty(AddressProperty).GetString());

        // The unaddressed receiver must stay silent — a broadcast would leak one
        // peer's public address to every other receiver on the share.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var buffer = new byte[ReceiveBufferSize];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await second.ReceiveAsync(buffer, cts.Token));
    }

    [Fact]
    public async Task ReceiverDisconnect_NotifiesSenderWithReceiverId()
    {
        var shareCode = Guid.NewGuid().ToString("n");
        using var sender = await ConnectAsync(shareCode);
        await RegisterSenderAsync(sender);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypeRegistered);

        var receiver = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(receiver);
        var receiverId = (await ReceiveOfTypeAsync(receiver, P2PProtocol.TypeRegistered))
            .GetProperty(P2PProtocol.PropReceiverId).GetString();
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);

        await receiver.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None);
        receiver.Dispose();

        var left = await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerLeft);
        Assert.Equal(receiverId, left.GetProperty(P2PProtocol.PropReceiverId).GetString());
    }

    [Fact]
    public async Task SenderDisconnect_NotifiesEveryReceiver()
    {
        var shareCode = Guid.NewGuid().ToString("n");
        var sender = await ConnectAsync(shareCode);
        await RegisterSenderAsync(sender);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypeRegistered);

        using var first = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(first);
        await ReceiveOfTypeAsync(first, P2PProtocol.TypeRegistered);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);

        using var second = await ConnectAsync(shareCode);
        await RegisterReceiverAsync(second);
        await ReceiveOfTypeAsync(second, P2PProtocol.TypeRegistered);
        await ReceiveOfTypeAsync(sender, P2PProtocol.TypePeerJoined);

        await sender.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None);
        sender.Dispose();

        // Both receivers need this: it is what turns a frozen progress bar into a
        // stated failure on the receiving page.
        await ReceiveOfTypeAsync(first, P2PProtocol.TypePeerLeft);
        await ReceiveOfTypeAsync(second, P2PProtocol.TypePeerLeft);
    }
}
