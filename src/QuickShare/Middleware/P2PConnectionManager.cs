using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuickShare.Middleware;

public sealed record P2PReceiverConnection
{
    public string Id { get; init; } = string.Empty;
    public WebSocket Socket { get; init; } = null!;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}

public sealed class P2PRoom
{
    public string ShareCode { get; init; } = string.Empty;
    public WebSocket? Sender { get; set; }
    public ConcurrentDictionary<string, P2PReceiverConnection> Receivers { get; init; } = new(StringComparer.Ordinal);
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; }
    public bool SenderConnected => Sender?.State == WebSocketState.Open;
    public bool ReceiverConnected => Receivers.Values.Any(r => r.Socket.State == WebSocketState.Open);
    public bool IsComplete => SenderConnected && Receivers.Count > 0;
}

public sealed class P2PConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, P2PRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private int _activeConnections;
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

    public bool TryAcceptConnection()
    {
        var current = Interlocked.Increment(ref _activeConnections);
        if (current <= _maxConnections)
            return true;

        Interlocked.Decrement(ref _activeConnections);
        return false;
    }

    public void ReleaseConnection()
    {
        Interlocked.Decrement(ref _activeConnections);
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

    public bool TryRegisterSender(string shareCode, WebSocket ws)
    {
        if (_activeConnections <= 0) return false;

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

    public (bool Success, string? ReceiverId) TryRegisterReceiver(string shareCode, WebSocket ws)
    {
        if (_activeConnections <= 0)
            return (false, null);

        var room = GetOrCreateRoom(shareCode);
        lock (room)
        {
            var receiverId = Guid.NewGuid().ToString("n")[..8];
            var entry = new P2PReceiverConnection
            {
                Id = receiverId,
                Socket = ws,
                JoinedAt = DateTime.UtcNow
            };
            room.Receivers[receiverId] = entry;
            room.LastActivity = DateTime.UtcNow;
            return (true, receiverId);
        }
    }

    public void RemoveRoom(string shareCode)
    {
        if (_rooms.TryRemove(shareCode, out var room))
        {
            lock (room)
            {
                SafeClose(room.Sender);
                foreach (var entry in room.Receivers.Values)
                {
                    SafeClose(entry.Socket);
                }
                room.Receivers.Clear();
                room.Sender = null;
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
            if (excludeRole != "receiver")
            {
                foreach (var recv in room.Receivers.Values)
                {
                    if (recv.Socket.State == WebSocketState.Open)
                        _ = SendToSocketAsync(recv.Socket, message, ct);
                }
            }
        }
        await Task.CompletedTask;
    }

    public string? RemoveReceiver(P2PRoom room, WebSocket ws)
    {
        lock (room)
        {
            var kvp = room.Receivers.FirstOrDefault(r => r.Value.Socket == ws);
            if (kvp.Value == null)
                return null;

            room.Receivers.TryRemove(kvp.Key, out _);
            room.LastActivity = DateTime.UtcNow;
            return kvp.Key;
        }
    }

    public string? FindReceiverId(P2PRoom room, WebSocket ws)
    {
        lock (room)
        {
            var kvp = room.Receivers.FirstOrDefault(r => r.Value.Socket == ws);
            return kvp.Value?.Id;
        }
    }

    public async Task SendToReceiverAsync(string shareCode, string receiverId, object message, CancellationToken ct)
    {
        if (!_rooms.TryGetValue(shareCode, out var room))
            return;

        if (!room.Receivers.TryGetValue(receiverId, out var entry))
            return;

        if (entry.Socket.State == WebSocketState.Open)
            await SendToSocketAsync(entry.Socket, message, ct);
    }

    public int GetActiveConnectionCount() => Interlocked.Add(ref _activeConnections, 0);

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
