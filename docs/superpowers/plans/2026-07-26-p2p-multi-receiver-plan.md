# P2P Multi-Receiver Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend P2P transfers from 1 receiver to N parallel receivers, each with their own WebRTC PeerConnection.

**Architecture:** `P2PRoom.Receiver` (single WebSocket) → `ConcurrentDictionary<string, P2PReceiverConnection>`. Sender manages one PeerConnection per receiver, streams file to all simultaneously. Signaling relays by `receiverId`.

**Tech Stack:** C# ASP.NET Core, Vanilla JS, WebRTC, WebSocket

## Global Constraints

- Each receiver gets their own PeerConnection + DataChannel + FileSender on the sender side
- Sender relays specify `receiverId` in offer/ICE messages; server routes to correct receiver
- Server adds `receiverId` when relaying answer/ICE from receiver → sender
- `receiverId` = 8-char hex string, generated on receiver registration
- `peer-joined` and `peer-left` events include `receiverId`
- No changes to receiver side (`download.js`, `fileReceiver.js`, `P2PDownload.razor`)
- Connection limit (`_activeConnections` counter) unchanged — each receiver socket counts as 1

---
### Task 1: P2PConnectionManager — P2PReceiverConnection + Receivers dictionary

**Files:**
- Modify: `src/QuickShare.Server/Middleware/P2PConnectionManager.cs`

**Interfaces:**
- Consumes: `P2PRoom` (existing class)
- Produces: `P2PReceiverConnection` record, updated `P2PRoom.Receivers`, updated `ReceiverConnected`/`IsComplete` properties

- [ ] **Step 1: Add P2PReceiverConnection record before P2PRoom**

```csharp
public sealed record P2PReceiverConnection
{
    public string Id { get; init; } = string.Empty;
    public WebSocket Socket { get; init; } = null!;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Replace `Receiver` property with `Receivers` dictionary**

```csharp
// Before
public WebSocket? Receiver { get; set; }

// After
public ConcurrentDictionary<string, P2PReceiverConnection> Receivers { get; init; } = new(StringComparer.Ordinal);
```

- [ ] **Step 3: Update `ReceiverConnected` and `IsComplete`**

```csharp
// Before
public bool ReceiverConnected => Receiver?.State == WebSocketState.Open;
public bool IsComplete => SenderConnected && ReceiverConnected;

// After
public bool ReceiverConnected => Receivers.Values.Any(r => r.Socket.State == WebSocketState.Open);
public bool IsComplete => SenderConnected && Receivers.Count > 0;
```

- [ ] **Step 4: Add `using System.Linq;` to imports**

Add at top of file.

- [ ] **Step 5: Build check**

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/QuickShare.Server/Middleware/P2PConnectionManager.cs
git commit -m "feat(p2p): change P2PRoom.Receiver to Receivers dictionary"
```

---
### Task 2: P2PConnectionManager — registration + removal

**Files:**
- Modify: `src/QuickShare.Server/Middleware/P2PConnectionManager.cs`

**Interfaces:**
- Consumes: `P2PReceiverConnection` (Task 1), `P2PRoom.Receivers` (Task 1)
- Produces: new `TryRegisterReceiver(shareCode, ws) → (bool, string?)`, updated `RemoveRoom`, new `RemoveReceiver(shareCode, ws) → string?`, new `FindReceiverId(room, ws) → string?`, new `SendToReceiverAsync(shareCode, receiverId, message, ct)`

- [ ] **Step 1: Update `TryRegisterReceiver` to support multiple receivers**

```csharp
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
```

- [ ] **Step 2: Add `RemoveReceiver` method**

```csharp
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
```

- [ ] **Step 3: Add `FindReceiverId` method**

```csharp
public string? FindReceiverId(P2PRoom room, WebSocket ws)
{
    lock (room)
    {
        var kvp = room.Receivers.FirstOrDefault(r => r.Value.Socket == ws);
        return kvp.Value?.Id;
    }
}
```

- [ ] **Step 4: Add `SendToReceiverAsync` method**

```csharp
public async Task SendToReceiverAsync(string shareCode, string receiverId, object message, CancellationToken ct)
{
    if (!_rooms.TryGetValue(shareCode, out var room))
        return;

    if (!room.Receivers.TryGetValue(receiverId, out var entry))
        return;

    if (entry.Socket.State == WebSocketState.Open)
        await SendToSocketAsync(entry.Socket, message, ct);
}
```

- [ ] **Step 5: Update `RemoveRoom` to close all receivers**

```csharp
// Before:
SafeClose(room.Receiver);
room.Receiver = null;

// After:
foreach (var entry in room.Receivers.Values)
{
    SafeClose(entry.Socket);
}
room.Receivers.Clear();
```

- [ ] **Step 6: Build check**

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/QuickShare.Server/Middleware/P2PConnectionManager.cs
git commit -m "feat(p2p): multi-receiver registration, removal, and relay helpers"
```

---
### Task 3: P2PSignalingMiddleware — register with receiverId + multi-receiver notification

**Files:**
- Modify: `src/QuickShare.Server/Middleware/P2PSignalingMiddleware.cs`

**Interfaces:**
- Consumes: `P2PConnectionManager.TryRegisterReceiver` (returns `(bool, string?)`), `P2PConnectionManager.FindReceiverId`, `room.Receivers` (dictionary)
- Produces: Updated `HandleRegisterAsync` that sends `receiverId` to receiver, `peer-joined` with `receiverId` to sender, file-meta to each receiver when sender comes online

- [ ] **Step 1: Update receiver registration in `HandleRegisterAsync`**

Replace the receiver block (starting at `else // receiver`):

```csharp
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
        // Notify sender about this new receiver
        await _manager.SendToSocketAsync(room.Sender!, new { type = "peer-joined", receiverId }, ct);

        // Send file meta to the new receiver
        await _manager.SendToSocketAsync(ws, new
        {
            type = "file-meta",
            fileName = room.FileName,
            fileSize = room.FileSize,
            mimeType = room.MimeType
        }, ct);
    }
    else
    {
        await _manager.SendToSocketAsync(ws, new { type = "waiting", message = "Waiting for sender to come online" }, ct);
    }
}
```

- [ ] **Step 2: Update sender registration — notify about all existing receivers**

After sender registers successfully (after the `if (room?.ReceiverConnected == true)` block), replace the notification block:

```csharp
// Before (single-receiver check):
if (room?.ReceiverConnected == true)
{
    await _manager.SendToSocketAsync(room.Sender!, new { type = "peer-joined" }, ct);
    await _manager.SendToSocketAsync(room.Receiver!, new
    {
        type = "file-meta",
        fileName = room.FileName,
        fileSize = room.FileSize,
        mimeType = room.MimeType
    }, ct);
}

// After (broadcast to all waiting receivers):
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
                mimeType = room.MimeType
            }, ct);
        }
        else
        {
            // Clean up stale receiver
            room.Receivers.TryRemove(recv.Id, out _);
        }
    }
}
```

- [ ] **Step 3: Build check**

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/QuickShare.Server/Middleware/P2PSignalingMiddleware.cs
git commit -m "feat(p2p): register returns receiverId, sender notified per-receiver"
```

---
### Task 4: P2PSignalingMiddleware — relay by receiverId + disconnect

**Files:**
- Modify: `src/QuickShare.Server/Middleware/P2PSignalingMiddleware.cs`

**Interfaces:**
- Consumes: `P2PConnectionManager.FindReceiverId`, `P2PConnectionManager.RemoveReceiver`, `room.Receivers`
- Produces: Updated `HandleRelayAsync` routes by receiverId; updated `InvokeAsync` disconnect handling for multi-receiver

- [ ] **Step 1: Update `HandleRelayAsync` to route by receiverId**

```csharp
private async Task HandleRelayAsync(string shareCode, WebSocket senderWs, string type, JsonElement root, CancellationToken ct)
{
    var room = _manager.GetRoom(shareCode);
    if (room == null) return;

    var innerData = root.TryGetProperty("data", out var d) ? d : root;

    if (room.Sender == senderWs)
    {
        // Sender → specific receiver: extract receiverId from root
        var receiverId = root.TryGetProperty("receiverId", out var rid) ? rid.GetString() : null;
        if (receiverId == null) return;

        await _manager.SendToReceiverAsync(shareCode, receiverId, new { type, data = innerData }, ct);
    }
    else
    {
        // Receiver → sender: find receiverId, add to message, relay to sender
        var recvId = _manager.FindReceiverId(room, senderWs);
        if (recvId == null || room.Sender?.State != WebSocketState.Open) return;

        await _manager.SendToSocketAsync(room.Sender, new { type, receiverId = recvId, data = innerData }, ct);
    }
}
```

- [ ] **Step 2: Update `NotifyPeer` for multi-receiver**

```csharp
private async Task NotifyPeer(P2PRoom room, string shareCode, string eventType, string disconnectedRole, string? receiverId = null)
{
    if (disconnectedRole == "sender")
    {
        // Notify all receivers that sender disconnected
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
        // Notify sender about specific receiver disconnecting
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
```

- [ ] **Step 3: Update disconnect handling in `InvokeAsync`**

Replace the disconnect block after the try-finally:

```csharp
_logger.LogInformation("WebSocket disconnected for share {ShareCode}", shareCode);
var room = _manager.GetRoom(shareCode);
if (room != null)
{
    lock (room)
    {
        var wasSender = room.Sender == ws;

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
```

- [ ] **Step 4: Build check**

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/QuickShare.Server/Middleware/P2PSignalingMiddleware.cs
git commit -m "feat(p2p): relay by receiverId, multi-receiver disconnect handling"
```

---
### Task 5: SignalingClient — receiverId in callbacks

**Files:**
- Modify: `src/QuickShare.Web/wwwroot/js/p2p/signaling.js`

**Interfaces:**
- Consumes: Server now sends `receiverId` in `peer-joined` and `peer-left` messages
- Produces: `onPeerJoined(receiverId)` and `onPeerLeft(message, receiverId)` callbacks

- [ ] **Step 1: Add `sendTo` method for sender messages with receiverId**

```javascript
SignalingClient.prototype.sendTo = function (type, payload, receiverId) {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
        if (this.onError) this.onError('WebSocket not connected');
        return;
    }
    this._sendMessage({ type: type, data: payload, receiverId: receiverId });
};
```

This puts `receiverId` at the ROOT level so the server can extract it for routing. The relayed message to the receiver will contain only `{ type, data }` — no receiverId leaked.

- [ ] **Step 2: Update `_handleMessage` to pass receiverId to callbacks**

In the `peer-joined` handler:

```javascript
case 'peer-joined':
    if (this.onPeerJoined) this.onPeerJoined(data.receiverId);
    break;
```

In the `peer-left` handler:

```javascript
case 'peer-left':
    if (this.onPeerLeft) this.onPeerLeft(data.message, data.receiverId);
    break;
```

Update the relay handlers (offer/answer/ice) to pass `receiverId` as third argument to `onRelay`:

```javascript
// Before:
case 'offer':
case 'answer':
case 'ice':
    if (this.onRelay) this.onRelay(data.type, data.data);
    break;

// After:
case 'offer':
case 'answer':
case 'ice':
    if (this.onRelay) this.onRelay(data.type, data.data, data.receiverId);
    break;
```

- [ ] **Step 3: Commit**

```bash
git add src/QuickShare.Web/wwwroot/js/p2p/signaling.js
git commit -m "feat(p2p): signaling.js passes receiverId in peer-joined/peer-left"
```

---
### Task 6: send.js — multi PeerConnection management

**Files:**
- Modify: `src/QuickShare.Web/wwwroot/js/p2p/send.js`

**Interfaces:**
- Consumes: `signaling.js` now calls `onPeerJoined(receiverId)`, `onPeerLeft(message, receiverId)`
- Produces: Multi-PeerConnection management, per-receiver FileSender instances, receiver count tracking

- [ ] **Step 1: Update state initialization**

```javascript
// Before
state._signaling: null, _peerConnection: null, _dataChannel: null, _sender: null, _encKey: null

// After
state._signaling: null, _peerConnections: {}, _channels: {}, _senders: {}, _receiverCount: 0, _encKey: null
```

- [ ] **Step 2: Update `onPeerJoined` in `connectSignaling`**

```javascript
state._signaling.onPeerJoined = function (receiverId) { onPeerJoined(receiverId); };
```

- [ ] **Step 3: Update `onRelay` in `connectSignaling`**

```javascript
state._signaling.onRelay = function (type, data, receiverId) {
    if (type === 'answer') onAnswer(data, receiverId);
    else if (type === 'ice' && data) {
        var pc = state._peerConnections[receiverId];
        if (pc) pc.addIceCandidate(data);
    }
};
```

- [ ] **Step 4: Add `onPeerLeft` in `connectSignaling`**

```javascript
state._signaling.onPeerLeft = function (message, receiverId) {
    if (receiverId) onReceiverLeft(receiverId);
};
```

- [ ] **Step 5: Rewrite `onPeerJoined` function**

```javascript
function onPeerJoined(receiverId) {
    if (state.phase === 'idle' || state.phase === 'failed') return;
    if (state.phase !== 'connecting' && state.phase !== 'transferring') {
        setPhase('connecting');
    }

    if (!window.P2P.PeerConnection) {
        state.error = 'P2P modules not loaded';
        setPhase('failed');
        return;
    }

    state._receiverCount++;
    updateReceiverCount();

    var pc = new window.P2P.PeerConnection();
    state._peerConnections[receiverId] = pc;

    pc.onIceCandidate = function (c) {
        if (state._signaling) {
            state._signaling.sendTo('ice', { candidate: c }, receiverId);
        }
    };
    pc.onConnectionState = function (s) {
        if ((s === 'failed' || s === 'disconnected') &&
            (state.phase === 'transferring' || state.phase === 'connecting')) {
            // Individual receiver failed — remove it, continue with others
            onReceiverLeft(receiverId);
        }
    };

    // Derive encryption key from PIN if set
    var encPromise = Promise.resolve();
    if (state.pin) {
        var salt = state.pin + state.shareCode;
        encPromise = window.P2P.encryption.deriveKey(state.pin, salt).then(function (key) {
            state._encKey = key;
        });
    }

    encPromise.then(function () {
        var channel = pc.createDataChannel('filetransfer');
        if (!channel) {
            onReceiverLeft(receiverId);
            return;
        }
        state._channels[receiverId] = channel;
        channel.onopen = function () { onChannelOpen(receiverId, channel); };
        channel.onerror = function () { onReceiverLeft(receiverId); };

        return pc.createOffer();
    })
    .then(function (sdp) {
        if (state._signaling) {
            state._signaling.sendTo('offer', { sdp: sdp }, receiverId);
        }
    })
    .catch(function (err) {
        onReceiverLeft(receiverId);
    });
}
```

- [ ] **Step 6: Update `onAnswer` to route by receiverId**

```javascript
function onAnswer(data, receiverId) {
    var sdp = data.sdp || data;
    var pc = state._peerConnections[receiverId];
    if (pc) {
        pc.setRemoteSdp(sdp).catch(function () {
            // Individual answer failed — receiver-specific error
        });
    }
}
```

- [ ] **Step 7: Rewrite `onChannelOpen` for per-receiver sender**

```javascript
function onChannelOpen(receiverId, channel) {
    if (!window.P2P.FileSender) {
        state.error = 'File sender module not loaded';
        setPhase('failed');
        return;
    }

    var enc = state._encKey ? { key: state._encKey, encrypt: window.P2P.encryption.encryptChunk } : null;
    var sender = new window.P2P.FileSender(state._file, channel, enc);
    state._senders[receiverId] = sender;

    sender.onProgress = function (sent, total) {
        // Track overall progress: average across all senders
        var totalSent = 0;
        var totalSize = 0;
        var keys = Object.keys(state._senders);
        for (var i = 0; i < keys.length; i++) {
            var s = state._senders[keys[i]];
            if (s && s.bytesSent !== undefined) {
                totalSent += s.bytesSent;
            }
            totalSize += state._file.size;
        }
        if (totalSize > 0) {
            state.progress = Math.round((totalSent / totalSize) * 100);
        }
        updateProgress();
    };
    sender.onComplete = function () {
        delete state._senders[receiverId];
        delete state._channels[receiverId];
        state._receiverCount--;

        if (state._receiverCount <= 0) {
            state.progress = 100;
            setPhase('success');
        }
        updateReceiverCount();
    };
    sender.onError = function (msg) {
        onReceiverLeft(receiverId);
    };

    if (state.phase !== 'transferring') {
        setPhase('transferring');
    }
    sender.start();
}
```

Wait — `FileSender` doesn't track `bytesSent` publicly. Let me adjust: use the `sent` param from `onProgress` and store per-sender progress.

Actually, the simplest approach: track each sender's progress individually. Store `state._senderProgress[receiverId] = progress`. Overall progress = average.

Let me simplify:

```javascript
function onChannelOpen(receiverId, channel) {
    if (!window.P2P.FileSender) {
        state.error = 'File sender module not loaded';
        setPhase('failed');
        return;
    }

    var enc = state._encKey ? { key: state._encKey, encrypt: window.P2P.encryption.encryptChunk } : null;
    var sender = new window.P2P.FileSender(state._file, channel, enc);
    state._senders[receiverId] = sender;
    state._senderProgress[receiverId] = 0;

    sender.onProgress = function (sent, total) {
        state._senderProgress[receiverId] = sent / total;
        // Overall = average of all sender progress
        var totalPct = 0;
        var count = 0;
        var keys = Object.keys(state._senderProgress);
        for (var i = 0; i < keys.length; i++) {
            totalPct += state._senderProgress[keys[i]];
            count++;
        }
        state.progress = count > 0 ? Math.round((totalPct / count) * 100) : 0;
        updateProgress();
    };
    sender.onComplete = function () {
        delete state._senders[receiverId];
        delete state._channels[receiverId];
        delete state._senderProgress[receiverId];
        state._receiverCount--;

        if (state._receiverCount <= 0) {
            state.progress = 100;
            setPhase('success');
        }
        updateReceiverCount();
    };
    sender.onError = function (msg) {
        onReceiverLeft(receiverId);
    };

    if (state.phase !== 'transferring') {
        setPhase('transferring');
    }
    sender.start();
}
```

And add `state._senderProgress: {}` to the state init.

- [ ] **Step 8: Add `onReceiverLeft` function**

```javascript
function onReceiverLeft(receiverId) {
    if (state._senders[receiverId]) {
        state._senders[receiverId].cancel();
        delete state._senders[receiverId];
    }
    if (state._channels[receiverId]) {
        delete state._channels[receiverId];
    }
    if (state._peerConnections[receiverId]) {
        state._peerConnections[receiverId].close();
        delete state._peerConnections[receiverId];
    }
    delete state._senderProgress[receiverId];
    state._receiverCount = Math.max(0, state._receiverCount - 1);
    updateReceiverCount();

    if (state._receiverCount <= 0 && (state.phase === 'transferring' || state.phase === 'connecting')) {
        state.phase = 'waiting';
        render();
    }
}
```

- [ ] **Step 9: Add `updateReceiverCount` function**

```javascript
function updateReceiverCount() {
    var el = document.getElementById('p2ps-receiver-count');
    var el2 = document.getElementById('p2ps-receiver-count-transfer');
    if (state._receiverCount > 0) {
        var text = state._receiverCount + ' receiver' + (state._receiverCount > 1 ? 's' : '') + ' connected';
        if (el) { el.textContent = text; el.style.display = ''; }
        if (el2) { el2.textContent = 'Sending to ' + state._receiverCount + ' receiver' + (state._receiverCount > 1 ? 's' : ''); el2.style.display = ''; }
    } else {
        if (el) el.style.display = 'none';
        if (el2) el2.style.display = 'none';
    }
}
```

- [ ] **Step 10: Update `cleanup()` to close all peer connections + cancel all senders**

```javascript
function cleanup() {
    var recvIds = Object.keys(state._senders);
    for (var i = 0; i < recvIds.length; i++) {
        if (state._senders[recvIds[i]]) state._senders[recvIds[i]].cancel();
    }
    state._senders = {};
    state._senderProgress = {};

    var pcIds = Object.keys(state._peerConnections);
    for (var i = 0; i < pcIds.length; i++) {
        if (state._peerConnections[pcIds[i]]) state._peerConnections[pcIds[i]].close();
    }
    state._peerConnections = {};
    state._channels = {};

    if (state._signaling) { state._signaling.close(); state._signaling = null; }
    state._encKey = null;
    state._receiverCount = 0;
}
```

- [ ] **Step 11: Add `_senderProgress: {}` to state initialization**

In the state object:

```javascript
var state = {
    phase: 'idle', progress: 0, shareCode: '', shareLink: '', error: '',
    pin: '', _file: null, _intentionalStop: false,
    _signaling: null, _peerConnections: {}, _channels: {}, _senders: {}, _senderProgress: {},
    _receiverCount: 0, _encKey: null
};
```

- [ ] **Step 12: Commit**

```bash
git add src/QuickShare.Web/wwwroot/js/p2p/send.js
git commit -m "feat(p2p): multi-PeerConnection sender with per-receiver FileSenders"
```

---
### Task 7: P2PTransfer.razor + CSS — receiver count UI

**Files:**
- Modify: `src/QuickShare.Web/Pages/P2PTransfer.razor`
- Modify: `src/QuickShare.Web/Pages/P2PTransfer.razor.css`

**Interfaces:**
- Consumes: `send.js` now calls `updateReceiverCount()` which sets `#p2ps-receiver-count` and `#p2ps-receiver-count-transfer` textContent
- Produces: Receiver count elements in waiting and transferring sections

- [ ] **Step 1: Add receiver count element to waiting section**

In `P2PTransfer.razor`, after the QR code div (line ~81), before the expiry text:

```html
<p id="p2ps-receiver-count" class="status-p" style="margin-top: 1rem; display: none"></p>
```

- [ ] **Step 2: Add receiver count element to transferring section**

In `P2PTransfer.razor`, after the progress bar div (line ~103), before the cancel button:

```html
<p id="p2ps-receiver-count-transfer" style="margin-top: 0.5rem; font-size: 0.875rem; color: var(--color-muted-foreground); display: none"></p>
```

- [ ] **Step 3: Commit**

```bash
git add src/QuickShare.Web/Pages/P2PTransfer.razor
git commit -m "feat(p2p): receiver count UI in waiting and transferring sections"
```

---
### Task 8: Build + verify

**Files:** None

- [ ] **Step 1: Build server**

Run: `dotnet build src/QuickShare.Server/QuickShare.Server.csproj --no-restore`
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Verify no JS syntax errors**

Run: `node -e "require('fs').readFileSync('src/QuickShare.Web/wwwroot/js/p2p/send.js', 'utf8')"` (basic syntax check — a real tool would use eslint)

Or just manually review the JS file reads cleanly.

- [ ] **Step 3: Commit any remaining**

```bash
git add -A
git commit -m "feat(p2p): enable multi-receiver support"
```

---
### Task 9: Update docs/p2p-architecture.md

**Files:**
- Modify: `docs/p2p-architecture.md`

**Interfaces:**
- Consumes: All changes from Tasks 1-8

- [ ] **Step 1: Update component map — P2PRoom model section (2.1)**

Change:
```
P2PRoom
├── Receiver        — WebSocket (null if not connected)
├── ReceiverConnected  — Receiver?.State == WebSocketState.Open
├── IsComplete         — both connected
```
To:
```
P2PRoom
├── Receivers          — ConcurrentDictionary<string, P2PReceiverConnection>
│   └── P2PReceiverConnection { Id, Socket, JoinedAt }
├── ReceiverConnected  — any receiver socket is open
├── IsComplete         — sender + at least 1 receiver
```

- [ ] **Step 2: Update register flow (2.2)**

Receiver register:
```
Receiver registers:
  1. TryRegisterReceiver → returns (bool, receiverId)
  2. Send { type: "registered", role: "receiver", receiverId }
  3. If sender online → notify sender ({ type: "peer-joined", receiverId })
     + send file-meta to new receiver
  4. If sender offline → send { type: "waiting" }
```

Sender register (when receivers exist):
```
Sender registers with existing receivers:
  1. For EACH connected receiver:
     - Send "peer-joined" with receiverId to sender
     - Send "file-meta" to that receiver
```

- [ ] **Step 3: Update relay flow**

```
Relay (offer/answer/ice):
  Sender → Server: { type, receiverId, data }
    → Server routes to that specific receiver's WebSocket
    → Receiver gets: { type, data }
  
  Receiver → Server: { type, data }
    → Server looks up receiver's ID from WebSocket
    → Sender gets: { type, receiverId, data }
    → Sender routes to the correct PeerConnection
```

- [ ] **Step 4: Update disconnect handling**

```
Peer disconnects:
  Sender disconnects
    → Notify ALL receivers: { type: "peer-left" }
  
  Receiver disconnects
    → Remove from room.Receivers
    → Notify sender: { type: "peer-left", receiverId }
    → Sender removes that receiver's PeerConnection + FileSender
```

- [ ] **Step 5: Update security model — room slot protection**

```
Before: Room slot protection: Only one sender + one receiver per share code
After:  Room slot protection: Only one sender, unlimited receivers per share code
```

- [ ] **Step 6: Commit**

```bash
git add docs/p2p-architecture.md
git commit -m "docs: update p2p-architecture.md for multi-receiver support"
```
