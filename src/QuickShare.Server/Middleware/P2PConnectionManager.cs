using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QuickShare.Server.Infrastructure;

namespace QuickShare.Server.Middleware;

public sealed class P2PRoom
{
    public string ShareCode { get; init; } = string.Empty;
    public WebSocket? Sender { get; set; }
    public WebSocket? Receiver { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; }
    public bool SenderConnected => Sender?.State == WebSocketState.Open;
    public bool ReceiverConnected => Receiver?.State == WebSocketState.Open;
    public bool IsComplete => SenderConnected && ReceiverConnected;
}

public sealed class P2PConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, P2PRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _connectionTokens = new(StringComparer.Ordinal);
    private readonly int _maxConnections;
    private readonly Timer _cleanupTimer;
    private readonly ILogger<P2PConnectionManager> _logger;
    private static readonly TimeSpan RoomTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    public P2PConnectionManager(IOptions<QuickShareOptions> options, ILogger<P2PConnectionManager> logger)
    {
        _maxConnections = options.Value.MaxP2PConnections;
        _logger = logger;
        _cleanupTimer = new Timer(_ => CleanupStaleRooms(), null, CleanupInterval, CleanupInterval);
    }

    public bool CanAcceptConnection()
    {
        return _connectionTokens.Count < _maxConnections;
    }

    public P2PRoom? GetRoom(string shareCode)
    {
        _rooms.TryGetValue(shareCode, out var room);
        return room;
    }

    public P2PRoom GetOrCreateRoom(string shareCode)
    {
        return _rooms.GetOrAdd(shareCode, key => new P2PRoom
        {
            ShareCode = key,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        });
    }

    public string GenerateConnectionToken(string shareCode)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        _connectionTokens.TryAdd(token, default);
        return token;
    }

    public bool TryRegisterSender(string shareCode, WebSocket ws)
    {
        if (!CanAcceptConnection()) return false;

        var room = GetOrCreateRoom(shareCode);
        lock (room)
        {
            if (room.SenderConnected)
                return false;

            room.Sender?.Dispose();
            room.Sender = ws;
            room.LastActivity = DateTime.UtcNow;
            return true;
        }
    }

    public bool TryRegisterReceiver(string shareCode, WebSocket ws)
    {
        if (!CanAcceptConnection()) return false;

        var room = GetOrCreateRoom(shareCode);
        lock (room)
        {
            if (room.ReceiverConnected)
                return false;

            room.Receiver?.Dispose();
            room.Receiver = ws;
            room.LastActivity = DateTime.UtcNow;
            return true;
        }
    }

    public void RemoveRoom(string shareCode)
    {
        if (_rooms.TryRemove(shareCode, out var room))
        {
            lock (room)
            {
                SafeClose(room.Sender);
                SafeClose(room.Receiver);
                room.Sender = null;
                room.Receiver = null;
            }
            _logger.LogInformation("Room {ShareCode} removed", shareCode);
        }
    }

    public Task SendToSocketAsync(WebSocket ws, object message, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open)
            return Task.CompletedTask;

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public async Task SendToPeerAsync(string shareCode, object message, string? excludeRole, CancellationToken ct)
    {
        if (!_rooms.TryGetValue(shareCode, out var room)) return;

        lock (room)
        {
            if (room.SenderConnected && excludeRole != "sender")
                _ = SendToSocketAsync(room.Sender!, message, ct);
            if (room.ReceiverConnected && excludeRole != "receiver")
                _ = SendToSocketAsync(room.Receiver!, message, ct);
        }
        await Task.CompletedTask;
    }

    public int GetActiveConnectionCount() => _connectionTokens.Count;

    private void CleanupStaleRooms()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, room) in _rooms)
        {
            if (room.SenderConnected || room.ReceiverConnected)
                continue;

            if (now - room.LastActivity > RoomTtl)
            {
                RemoveRoom(key);
            }
        }
    }

    private static void SafeClose(WebSocket? ws)
    {
        if (ws?.State == WebSocketState.Open || ws?.State == WebSocketState.CloseReceived)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Room closed", cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort close
            }
        }
        ws?.Dispose();
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var (key, room) in _rooms)
        {
            RemoveRoom(key);
        }
    }
}
