using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace QuickShare.Infrastructure;

public sealed class P2PSignalingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly P2PConnectionManager _manager;
    private readonly ILogger<P2PSignalingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, string> _roles = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const string WsPathPrefix = "/ws/connect/";

    public P2PSignalingMiddleware(RequestDelegate next, P2PConnectionManager manager, ILogger<P2PSignalingMiddleware> logger)
    {
        _next = next;
        _manager = manager;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(WsPathPrefix, StringComparison.OrdinalIgnoreCase) || !context.WebSockets.IsWebSocketRequest)
        {
            await _next(context);
            return;
        }

        var shareCode = path[WsPathPrefix.Length..];

        if (!_manager.TryAcceptConnection())
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Server at capacity");
            return;
        }

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        _logger.LogInformation("WebSocket connected for share {ShareCode}", shareCode);

        try
        {
            await HandleConnectionAsync(ws, shareCode, context.RequestAborted);
        }
        finally
        {
            _manager.ReleaseConnection();
        }

        _logger.LogInformation("WebSocket disconnected for share {ShareCode}", shareCode);
        var room = _manager.GetRoom(shareCode);
        if (room != null)
        {
            bool wasSender;
            lock (room)
            {
                wasSender = room.Sender == ws;

                if (wasSender)
                {
                    room.Sender = null;
                }
            }

            if (wasSender)
            {
                _ = NotifyPeer(room, shareCode, "peer-left", "sender");
            }
            else
            {
                var disconnectedId = _manager.RemoveReceiver(room, ws);
                if (disconnectedId != null)
                {
                    _ = NotifyPeer(room, shareCode, "peer-left", "receiver", disconnectedId);
                }
            }
        }
    }

    private async Task HandleConnectionAsync(WebSocket ws, string shareCode, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1024 * 64]; // 64KB receive buffer

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                // Handle fragmented messages
                if (!result.EndOfMessage)
                {
                    var fullMsg = new StringBuilder(text);
                    while (!result.EndOfMessage)
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        fullMsg.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    text = fullMsg.ToString();
                }

                var connectionId = shareCode + "_" + ws.GetHashCode();
                await HandleMessageAsync(ws, shareCode, connectionId, text, ct);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error for share {ShareCode}", shareCode);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            var connId = shareCode + "_" + ws.GetHashCode();
            _roles.TryRemove(connId, out _);
        }
    }

    private async Task HandleMessageAsync(WebSocket ws, string shareCode, string connectionId, string text, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var type = typeProp.GetString() ?? "";

            switch (type)
            {
                case "register":
                    await HandleRegisterAsync(ws, shareCode, connectionId, root, ct);
                    break;

                case "offer":
                case "answer":
                case "ice":
                    await HandleRelayAsync(shareCode, ws, type, root, ct);
                    break;

                default:
                    _logger.LogWarning("Unknown message type {Type} from share {ShareCode}", type, shareCode);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON from share {ShareCode}", shareCode);
            await _manager.SendToSocketAsync(ws, new { type = "error", message = "Invalid JSON" }, ct);
        }
    }

    private async Task HandleRegisterAsync(WebSocket ws, string shareCode, string connectionId, JsonElement root, CancellationToken ct)
    {
        var role = root.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : null;
        if (role is not ("sender" or "receiver"))
        {
            await _manager.SendToSocketAsync(ws, new { type = "error", message = "Role must be 'sender' or 'receiver'" }, ct);
            return;
        }

        _roles[connectionId] = role;

        if (role == "sender")
        {
            if (!_manager.TryRegisterSender(shareCode, ws))
            {
                await _manager.SendToSocketAsync(ws, new { type = "error", message = "A sender is already registered for this share" }, ct);
                return;
            }

            if (root.TryGetProperty("fileMeta", out var meta))
            {
                var currentRoom = _manager.GetRoom(shareCode);
                if (currentRoom != null)
                {
                    lock (currentRoom)
                    {
                        currentRoom.FileName = meta.TryGetProperty("name", out var n) ? n.GetString() : null;
                        currentRoom.FileSize = meta.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : null;
                        currentRoom.MimeType = meta.TryGetProperty("mime", out var m) ? m.GetString() : null;
                        currentRoom.HasPin = meta.TryGetProperty("hasPin", out var hp) && hp.GetBoolean();
                        currentRoom.PinHash = meta.TryGetProperty("pinHash", out var ph) && ph.ValueKind != JsonValueKind.Null ? ph.GetString() : null;
                    }
                }
            }

            await _manager.SendToSocketAsync(ws, new { type = "registered", role = "sender" }, ct);

            var room = _manager.GetRoom(shareCode);
            if (room != null)
            {
                P2PReceiverConnection[] existingReceivers;
                lock (room)
                {
                    existingReceivers = room.Receivers.Values.ToArray();
                }

                foreach (var recv in existingReceivers)
                {
                    if (recv.Socket.State == WebSocketState.Open)
                    {
                        await _manager.SendToSocketAsync(room.Sender!, new { type = "peer-joined", receiverId = recv.Id }, ct);
                        await _manager.SendToSocketAsync(recv.Socket, new
                        {
                            type = "file-meta",
                            fileName = room.FileName,
                            fileSize = room.FileSize,
                            mimeType = room.MimeType,
                            hasPin = room.HasPin,
                            pinHash = room.PinHash
                        }, ct);
                    }
                    else
                    {
                        room.Receivers.TryRemove(recv.Id, out _);
                    }
                }
            }
        }
        else // receiver
        {
            var (success, receiverId) = _manager.TryRegisterReceiver(shareCode, ws);
            if (!success || receiverId == null)
            {
                await _manager.SendToSocketAsync(ws, new { type = "error", message = "Failed to register receiver" }, ct);
                return;
            }

            var room = _manager.GetRoom(shareCode);
            var hasSender = room?.SenderConnected == true;

            await _manager.SendToSocketAsync(ws, new { type = "registered", role = "receiver", receiverId }, ct);

            if (hasSender && room != null)
            {
                await _manager.SendToSocketAsync(room.Sender!, new { type = "peer-joined", receiverId }, ct);
                await _manager.SendToSocketAsync(ws, new
                {
                    type = "file-meta",
                    fileName = room.FileName,
                    fileSize = room.FileSize,
                    mimeType = room.MimeType,
                    hasPin = room.HasPin,
                    pinHash = room.PinHash
                }, ct);
            }
            else
            {
                await _manager.SendToSocketAsync(ws, new { type = "waiting", message = "Waiting for sender to come online" }, ct);
            }
        }
    }

    private async Task HandleRelayAsync(string shareCode, WebSocket senderWs, string type, JsonElement root, CancellationToken ct)
    {
        var room = _manager.GetRoom(shareCode);
        if (room == null) return;

        var innerData = root.TryGetProperty("data", out var d) ? d : root;

        if (room.Sender == senderWs)
        {
            var receiverId = root.TryGetProperty("receiverId", out var rid) ? rid.GetString() : null;
            if (receiverId == null) return;

            await _manager.SendToReceiverAsync(shareCode, receiverId, new { type, data = innerData }, ct);
        }
        else
        {
            var recvId = _manager.FindReceiverId(room, senderWs);
            if (recvId == null || room.Sender?.State != WebSocketState.Open) return;

            await _manager.SendToSocketAsync(room.Sender, new { type, receiverId = recvId, data = innerData }, ct);
        }
    }

    private async Task NotifyPeer(P2PRoom room, string shareCode, string eventType, string disconnectedRole, string? receiverId = null)
    {
        if (disconnectedRole == "sender")
        {
            foreach (var recv in room.Receivers.Values)
            {
                if (recv.Socket.State == WebSocketState.Open)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _manager.SendToSocketAsync(recv.Socket, new { type = eventType, message = "sender disconnected" }, cts.Token);
                    }
                    catch { /* best-effort */ }
                }
            }
        }
        else if (disconnectedRole == "receiver" && receiverId != null)
        {
            if (room.Sender?.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _manager.SendToSocketAsync(room.Sender, new { type = eventType, receiverId, message = "receiver disconnected" }, cts.Token);
                }
                catch { /* best-effort */ }
            }
        }
    }
}
