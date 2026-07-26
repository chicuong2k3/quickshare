# P2P Multi-Receiver Support — Design Spec

## Overview

Currently each `P2PRoom` supports exactly **1 receiver**. This spec extends it to support **N parallel receivers** — each gets their own WebRTC PeerConnection + DataChannel, and the sender streams the file to all connected receivers simultaneously.

## 1. Server — Room Model

### 1.1 P2PReceiverConnection

New record to track individual receiver connections:

```csharp
public sealed record P2PReceiverConnection
{
    public string Id { get; init; } = string.Empty;       // 8-char hex, generated on register
    public WebSocket Socket { get; init; } = null!;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}
```

### 1.2 P2PRoom Changes

```csharp
// Before
public WebSocket? Receiver { get; set; }

// After
public ConcurrentDictionary<string, P2PReceiverConnection> Receivers { get; init; } = new();
```

Derived properties:

| Property | Before | After |
|----------|--------|-------|
| `ReceiverConnected` | `Receiver?.State == Open` | `Receivers.Values.Any(r => r.Socket.State == Open)` |
| `IsComplete` | `SenderConnected && ReceiverConnected` | `SenderConnected && Receivers.Count > 0` |

### 1.3 P2PConnectionManager — Registration

`TryRegisterReceiver` changes:
- Always succeeds (removes the `if (ReceiverConnected) return false` guard)
- Generates `receiverId` = 8-char hex via `RandomNumberGenerator`
- Stores `new P2PReceiverConnection { Id, Socket = ws }` in `Receivers`
- Returns the `receiverId` so caller can include it in `registered` response

`TryRegisterSender` — unchanged.

### 1.4 P2PConnectionManager — Relay

`SendToReceiverAsync(shareCode, receiverId, message, ct)` — new method:
- Lookup room → lookup `Receivers[receiverId]` → `SendToSocketAsync`

`SendToAllReceiversAsync(shareCode, message, ct)` — new method:
- Iterate `Receivers.Values`, send to each open socket

`SendToPeerAsync` — unchanged (still broadcasts to sender + all receivers).

### 1.5 P2PConnectionManager — Disconnect & Cleanup

`RemoveRoom` changes:
- Close all receiver sockets: `foreach (var r in room.Receivers.Values) SafeClose(r.Socket)`

Individual receiver disconnect:
- Remove from `room.Receivers` by `receiverId`
- Notify sender: `{ type: "peer-left", receiverId }`
- If no receivers left and sender disconnected → room is stale, cleanup will remove

`ReceiverConnected` becomes: `Receivers.Values.Any(r => r.Socket.State == WebSocketState.Open)`

### 1.6 P2PSignalingMiddleware — HandleRegisterAsync

After registering a receiver, the `registered` message now includes `receiverId`:

```json
{ "type": "registered", "role": "receiver", "receiverId": "a1b2c3d4" }
```

This lets the receiver JS know its own ID (not strictly needed for receiver, but useful).

The `peer-joined` message to sender now includes `receiverId`:

```json
{ "type": "peer-joined", "receiverId": "a1b2c3d4" }
```

This lets the sender create a dedicated PeerConnection for that receiver.

### 1.7 P2PSignalingMiddleware — HandleRelayAsync

**Before:** Determine target peer based on senderWs == Sender vs Receiver (single).

**After:**

| Sender is | Action |
|-----------|--------|
| `room.Sender` | Extract `receiverId` from root → relay to that specific receiver |
| Receiver | Look up which receiver entry matches senderWs → relay to `room.Sender` |

**Sender → Receiver relay:**
```json
// Sender sends: { "type": "offer", "receiverId": "a1b2c3d4", "data": { sdp: "..." } }
// Server extracts receiverId, routes to that receiver:
{ "type": "offer", "data": { sdp: "..." } }
```

**Receiver → Sender relay:**
```json
// Receiver sends: { "type": "answer", "data": { sdp: "..." } }
// Server adds receiverId, routes to sender:
{ "type": "answer", "receiverId": "a1b2c3d4", "data": { sdp: "..." } }
```

This lets the sender JS route answer/ICE to the correct PeerConnection.

## 2. Client — Signaling Protocol

New fields:

| Message | From | New Field |
|---------|------|-----------|
| `registered` | Server → Receiver | `receiverId: string` |
| `peer-joined` | Server → Sender | `receiverId: string` |
| `offer` (relayed) | Sender → Server → Receiver | `receiverId: string` in sender's message |
| `answer` (relayed) | Receiver → Server → Sender | `receiverId: string` added by server |
| `ice` (relayed) | Receiver → Server → Sender | `receiverId: string` added by server |
| `peer-left` | Server → Sender | `receiverId: string` |

## 3. Client — Sender JS (`send.js`)

### 3.1 State Changes

```javascript
// Before
state._peerConnection, state._dataChannel, state._sender

// After
state._peerConnections = {}  // { receiverId: PeerConnection }
state._channels = {}         // { receiverId: RTCDataChannel }
state._senders = {}          // { receiverId: FileSender }
state._receiverCount = 0     // number of connected receivers
```

### 3.2 Flow

**`onPeerJoined(receiverId)`:**
1. Set phase = `'connecting'` (only if first receiver — or keep existing phase)
2. Create new `PeerConnection()` → `state._peerConnections[receiverId]`
3. Wire `onIceCandidate`: send with `receiverId: receiverId`
4. Wire `onConnectionState`: handle failed/disconnected per-receiver
5. Derive encryption key (same key, reused)
6. Create `DataChannel('filetransfer')` → `state._channels[receiverId]`
7. `channel.onopen` → create `FileSender(file, channel, enc)` → `state._senders[receiverId]`
   → if sender is the first, start transfer immediately
   → if sender is Nth (transfer already ongoing), start immediately too
8. Create offer → send `{ type: 'offer', receiverId, data: { sdp } }`

**`onRelay(type, data)`:**
- type = `'answer'`: `state._peerConnections[data.receiverId].setRemoteSdp(data.sdp)`
- type = `'ice'`: `state._peerConnections[data.receiverId].addIceCandidate(data.candidate)`

**`onPeerLeft(receiverId)`:**
- Close and remove `state._peerConnections[receiverId]`
- Cancel `state._senders[receiverId]`
- Decrement `state._receiverCount`
- If `_receiverCount === 0` → show waiting phase

**`cleanup()`:**
- Iterate all `_peerConnections`, close each
- Cancel all `_senders`

### 3.3 UI

In `waiting` phase:
- Show receiver count: "N receivers connected" (when `_receiverCount > 0`)
- Show receiver count updating as they join/leave

In `transferring` phase:
- Show overall progress (based on the "slowest" receiver, or average)
- Show receiver count: "Sending to N receivers"

## 4. Client — Receiver JS (`download.js`)

**No changes needed.** Each receiver manages exactly one connection, which is the current behavior. The receiver ID is handled transparently by the server: receiver receives the `registered` message with their ID but doesn't need to use it, and the server strips/adds `receiverId` during relay automatically.

### SignalingClient Changes (`signaling.js`)

- `onPeerJoined` callback now receives `receiverId` parameter (from `data.receiverId`)
- `onPeerLeft` callback now receives `receiverId`

## 5. UI — Sender Page (`P2PTransfer.razor`)

Add to `waiting` section:
```html
<p id="p2ps-receiver-count" class="status-p" style="margin-top: 1rem; display: none">
  0 receivers connected
</p>
```

Add to `transferring` section:
```html
<p id="p2ps-receiver-count-transfer" style="margin-top: 0.5rem; font-size: 0.875rem; color: var(--color-muted-foreground)">
  Sending to 0 receivers
</p>
```

No other UI changes needed — progress bar already exists and shows overall progress.

## 6. Server — Connection Limit

The global `_activeConnections` counter via `TryAcceptConnection()`/`ReleaseConnection()` is unchanged. Each receiver WebSocket consumes one slot. Sender WebSocket consumes one slot. So N receivers = N+1 connections.

## 7. Edge Cases

| Case | Behavior |
|------|----------|
| 2 receivers join simultaneously | Both handled independently; each gets their own PC/offer/transfer |
| Receiver joins mid-transfer | Sender creates new PC + offer for them; `FileSender` starts from beginning of file; existing receivers unaffected |
| All receivers disconnect | Sender goes back to `waiting` phase |
| Sender disconnects | All receivers get `peer-left` error |
| Receiver disconnect during transfer | Sender cancels that receiver's FileSender, removes PC; continues sending to others |
| PIN-protected file | Key derived once per receiver (same PIN) — each receiver enters PIN independently |
| Very large file with N receivers | N parallel `FileSender` instances reading from the same `File` object — browser handles concurrent reads; limited by sender's upload bandwidth |

## 8. Files to Change

| File | Changes |
|------|---------|
| `P2PConnectionManager.cs` | P2PReceiverConnection record, Receivers dictionary, TryRegisterReceiver (multi), SendToReceiverAsync, SendToAllReceiversAsync, relay methods |
| `P2PSignalingMiddleware.cs` | Register returns receiverId, relay routes by receiverId, peer-joined/peer-left include receiverId |
| `send.js` | Multi PeerConnection/FileSender management, receiverId-aware relay |
| `signaling.js` | onPeerJoined/onPeerLeft pass receiverId |
| `P2PTransfer.razor` | Receiver count UI in waiting + transferring sections |
| `P2PTransfer.razor.css` | Optional styling for receiver count text |
| `docs/p2p-architecture.md` | Update room model, relay flow, diagrams |
