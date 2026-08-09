# QuickShare — P2P Transfer Architecture

## Overview

QuickShare supports **direct peer-to-peer file transfers** via WebRTC, bypassing the server entirely for file data. The server acts only as a **signaling relay** — it creates share links, coordinates WebSocket connections between sender and receiver, and relays WebRTC SDP/ICE messages. The file bytes never touch the server.

> Sender → Server (metadata + signaling) → Receiver → WebRTC Data Channel (direct) → File delivered.

---

## 1. Component Map

| Layer | Project | File | Role |
|-------|---------|------|------|
| Share Link Creation | `QuickShare` | `Features/Shares/P2P/CreateP2PShareEndpoint.cs` | `POST /api/shares/p2p` — creates share code + Delivery record. Base URL received via client callback. |
| Connection Manager | `QuickShare` | `Middleware/P2PConnectionManager.cs` | Room lifecycle, sender/receiver WebSocket tracking, connection limits |
| Signaling Middleware | `QuickShare` | `Middleware/P2PSignalingMiddleware.cs` | WebSocket endpoint `/ws/connect/{shareCode}` — register, relay offer/answer/ICE |
| Server WebSocket Config | `QuickShare` | `Program.cs` | `UseWebSockets` + `P2PSignalingMiddleware` + `P2PConnectionManager` singleton |
| Sender Page UI | `QuickShare` | `Pages/P2PTransfer.razor` | Sender page at `/p2p/send` — drag & drop, share link generation |
| Sender Page JS | `QuickShare` | `wwwroot/js/p2p/send.js` | Coordinates file selection, share creation, SignalingClient + PeerConnection + FileSender |
| Download Page UI | `QuickShare` | `Pages/P2PDownload.razor` | Receiver page at `/p2p/d/{ShareCode}` |
| Download Page JS | `QuickShare` | `wwwroot/js/p2p/download.js` | Coordinates SignalingClient + PeerConnection + FileReceiver |
| Signaling Client | `QuickShare` | `wwwroot/js/p2p/signaling.js` | WebSocket connection, auto-reconnect, message relay callbacks |
| Peer Connection | `QuickShare` | `wwwroot/js/p2p/peerConnection.js` | RTCPeerConnection wrapper — offer, answer, ICE candidates, data channels |
| File Sender | `QuickShare` | `wwwroot/js/p2p/fileTransfer.js` | Chunked file send over WebRTC data channel with backpressure |
| File Receiver | `QuickShare` | `wwwroot/js/p2p/fileReceiver.js` | Chunked file receive — blob mode (<500MB) or StreamSaver (>500MB) |

---

## 2. Server Components

### 2.1 P2PConnectionManager

Singleton service managing all active P2P rooms.

```
P2PRoom
├── ShareCode     — unique lookup key (16-char hex)
├── Sender        — WebSocket (null if not connected)
├── Receivers     — ConcurrentDictionary<string, P2PReceiverConnection>
│   └── P2PReceiverConnection { Id (8-char hex), Socket, JoinedAt }
├── FileName      — set by sender during register
├── FileSize      — set by sender during register
├── MimeType      — set by sender during register
├── CreatedAt     — room creation timestamp
├── LastActivity  — bumped on sender/receiver registration
├── SenderConnected    — Sender?.State == WebSocketState.Open
├── ReceiverConnected  — any receiver socket is open (Receivers.Values.Any(r => r.Socket.State == Open))
└── IsComplete         — sender + at least 1 receiver
```

Key behaviors:
- **Rooms** stored in `ConcurrentDictionary<string, P2PRoom>` (case-insensitive share code)
- **Connection tokens** tracked in `ConcurrentDictionary<string, byte>` for capacity limit
- **Capacity**: `MaxP2PConnections` (configurable via `QuickShareOptions`)
- **Cleanup**: Timer every 30s removes rooms with no active connections and >5 min stale
- **Thread safety**: Room mutation uses `lock(room)`

### 2.2 P2PSignalingMiddleware

ASP.NET Core middleware intercepting `GET /ws/connect/{shareCode}` WebSocket upgrade requests.

**Message types handled:**

| Type | Direction | Purpose |
|------|-----------|---------|
| `register` | Client → Server | Declare role (`sender`/`receiver`) and file metadata |
| `offer` | Client → Server | Sender's WebRTC SDP offer — relayed to receiver |
| `answer` | Client → Server | Receiver's WebRTC SDP answer — relayed to sender |
| `ice` | Client → Server | ICE candidate — relayed to the other peer |

**Register flow:**
```
Sender registers:
  1. TryRegisterSender(shareCode, ws) — fails if sender already connected
  2. Extract file metadata (name, size, mime) from register message
  3. Store metadata in P2PRoom
  4. Send { type: "registered", role: "sender" } back
  5. If receivers exist → for EACH receiver: notify sender ("peer-joined" with receiverId) + send file-meta to receiver

Receiver registers:
  1. TryRegisterReceiver(shareCode, ws) → returns (bool, receiverId = 8-char hex)
  2. Send { type: "registered", role: "receiver", receiverId } back
  3. If sender already connected → notify sender ("peer-joined" with receiverId) + send file-meta to receiver
  4. If sender not connected → send { type: "waiting" }
```

**Disconnect handling:**
- **Sender disconnects**: Notify ALL receivers with `{ type: "peer-left", message: "sender disconnected" }`
- **Receiver disconnects**: Remove from `room.Receivers`, notify sender with `{ type: "peer-left", receiverId, message: "receiver disconnected" }`
- Role tracked per connectionId (shareCode + ws.GetHashCode())

**Relay (offer/answer/ice):**
- Sender → Server: `{ type, receiverId, data }` → Server routes to that specific receiver's WebSocket → Receiver gets `{ type, data }`
- Receiver → Server: `{ type, data }` → Server looks up receiver's ID from WebSocket → Sender gets `{ type, receiverId, data }` → Sender routes to the correct PeerConnection

---

## 3. Client-Side JavaScript Modules

All modules registered under `window.P2P` namespace.

### 3.1 SignalingClient (`window.P2P.SignalingClient`)

- **Constructor**: `(apiBaseUrl, shareCode)`
- **`connect(role, fileMeta)`**: Opens WebSocket to `ws(s)://{apiBase}/ws/connect/{shareCode}`, sends `register` on open
- **Auto-reconnect**: Up to 3 attempts with exponential backoff (1s, 2s, 4s)
- **`send(type, payload)`**: Sends `{ type, data }` JSON message
- **`sendTo(type, payload, receiverId)`**: Sends `{ type, data, receiverId }` — puts receiverId at root level for server routing
- **`close()`**: Graceful shutdown

**Inbound message dispatch:**

| Server Message | Callback |
|---------------|----------|
| `registered` | `onRegistered(role)` |
| `peer-joined` | `onPeerJoined(receiverId)` |
| `file-meta` | `onFileMeta({ fileName, fileSize, mimeType })` |
| `waiting` | `onWaiting(message)` |
| `peer-left` | `onPeerLeft(message, receiverId)` |
| `error` | `onError(message)` |
| `offer` / `answer` / `ice` | `onRelay(type, data, receiverId)` |

### 3.2 PeerConnection (`window.P2P.PeerConnection`)

WebRTC wrapper using Google's public STUN server.

- **ICE servers**: `stun:stun.l.google.com:19302`
- **`createOffer()`**: Creates SDP offer → sets local description → returns SDP string
- **`createAnswer(offerSdp)`**: Sets remote description → creates answer → sets local → returns SDP
- **`setRemoteSdp(sdp)`**: Sets remote answer SDP
- **`addIceCandidate(candidate)`**: Adds ICE candidate (buffers until remote description set)
- **`createDataChannel(label)`**: Creates WebRTC data channel for file transfer
- **Pending ICE candidates**: Buffered until `remoteSet` is true, then flushed in batch

### 3.3 FileTransfer (`window.P2P.FileSender`)

Sender-side chunked upload over the data channel.

- **Chunk size**: 16 KB
- **Backpressure**: Pauses sending when `bufferedAmount > 1MB`, resumes at 512KB
- **Message format**: 8-byte header + chunk data
  ```
  [4 bytes: chunk index (little-endian uint32)]
  [1 byte:  final flag (0/1)]
  [2 bytes: data length (little-endian uint16)]
  [1 byte:  reserved]
  [N bytes: chunk data]
  ```
- **Flow**: Send JSON metadata → read file in chunks → prepend 8-byte header → send over data channel

### 3.4 FileReceiver (`window.P2P.FileReceiver`)

Receiver-side chunked download from the data channel.

- **Blob mode** (<500MB): Accumulates chunks in memory, downloads via blob URL
- **StreamSaver mode** (>500MB): Lazy-loads StreamSaver.js from CDN, streams chunks to disk
- **Chunk reordering**: Each chunk self-identifies via header index — handles out-of-order arrival
- **Header parsing**: Reads chunk index, final flag, and data length from 8-byte header

### 3.5 Transfer Security

P2P transfers are **direct and unencrypted at the application layer** — PIN and client-side encryption were removed (commit `3062a69`). Chunks travel over the WebRTC data channel in plaintext. Transit is protected only by WebRTC's built-in DTLS; the signaling server never sees file bytes.

---

## 4. Full P2P Transfer Flow

### 4.1 Share Link Creation (Sender)

The Web client sends `window.location.origin` as `BaseUrl` in the request body. The server uses it directly to build the share link — no config or hostname guessing needed.

```
Sender                          Server
  │                                │
  │  POST /api/shares/p2p          │
  │  { fileName, fileSize,         │
  │    mimeType,                   │
  │    baseUrl }                   │
  │───────────────────────────────▶│
  │                                │── Generate shareCode (16 hex chars)
  │                                │── Create Delivery record (ExpiresAt = +7 days)
  │                                │── Build shareLink from baseUrl + /p2p/d/{code}
  │                                │
  │◀───────────────────────────────│
  │  201 Created                   │
  │  { shareCode, shareLink }      │
  │                                │
  │  Sender copies link and        │
  │  sends to receiver             │
```

### 4.2 Direct Transfer (WebRTC)

```
Sender Browser           Server (Signaling)              Receiver Browser
  │                         │                                │
  │  WebSocket connect      │                                │
  │  ws://.../connect/{code}│                                │
  │────────────────────────▶│                                │
  │                         │                                │
  │  { type: "register",    │                                │
  │    role: "sender",      │                                │
  │    fileMeta: {...} }    │                                │
  │────────────────────────▶│                                │
  │                         │── Store fileMeta in room       │
  │  { type: "registered",  │                                │
  │    role: "sender" }     │                                │
  │◀────────────────────────│                                │
  │                         │                                │
  │                         │                                │  User opens /p2p/d/{code}
  │                         │                                │  Page loads → init()
  │                         │                                │
  │                         │  WebSocket connect             │
  │                         │  ws://.../connect/{code}       │
  │                         │◀───────────────────────────────│
  │                         │                                │
  │                         │  { type: "register",           │
  │                         │    role: "receiver" }          │
  │                         │◀───────────────────────────────│
  │                         │                                │
  │                         │── Receiver registered          │
  │  { type: "peer-joined" }│                                │
  │◀────────────────────────│                                │
  │                         │  { type: "file-meta",          │
  │                         │    fileName, fileSize,         │
  │                         │    mimeType }                  │
  │                         │───────────────────────────────▶│
  │                         │                                │  Shows file info
  │                         │                                │  User clicks "Download"
  │                         │                                │
  │  === WebRTC Signaling Relay ===                          │
  │                         │                                │
  │  { type: "offer",       │                                │
  │    data: { sdp } }      │                                │
  │────────────────────────▶│                                │
  │                         │  { type: "offer",              │
  │                         │    data: { sdp } }             │
  │                         │───────────────────────────────▶│
  │                         │                                │── PeerConnection
  │                         │                                │   creates answer
  │                         │  { type: "answer",             │
  │                         │    data: { sdp } }             │
  │                         │◀───────────────────────────────│
  │  { type: "answer",      │                                │
  │    data: { sdp } }      │                                │
  │◀────────────────────────│                                │
  │                         │                                │
  │  === ICE Candidates Exchanged (bidirectional relay) ===  │
  │                         │                                │
  │  ◀═══════════ ICE candidates relayed ═══════════════▶   │
  │                         │                                │
  │  === WebRTC Peer Connection Established ===              │
  │                         │                                │
  │  ╔══════════ Direct P2P Data Channel ═══════════╗        │
  │  ║  Sender reads file in 16KB chunks           ║        │
  │  ║  Prepends 8-byte header → sends raw chunk   ║        │
  │  ║══════════════════════════════════════════════╣        │
  │  ║  Receiver parses header → reassembles blob  ║        │
  │  ╚══════════════════════════════════════════════╝        │
  │                         │                                │
  │                         │  File downloaded.              │
  │                         │  Server never saw the data.    │
  │                         │                                │
```

### 4.4 Disconnect / Room Cleanup

```
Peer disconnects
  → WebSocket closes
  → Signaling middleware detects, notifies other peer: { type: "peer-left" }
  → Other peer shows "Connection lost"

Room timeout
  → CleanupStaleRooms() runs every 30s
  → Removes rooms with no active connections AND LastActivity > 5 min
  → Force-closes any remaining sockets
```

---

## 5. Download Page States

The download page at `/p2p/d/{ShareCode}` renders one section at a time:

| State | Section ID | Trigger |
|-------|-----------|---------|
| **checking** | `p2pd-checking` | Page load — spinner + "Connecting to sender..." |
| **waiting-sender** | `p2pd-waiting` | Receiver connected but sender offline |
| **ready** | `p2pd-ready` | File metadata received, ready to download |
| **connecting** | `p2pd-ready` (btn disabled) | WebRTC handshake in progress |
| **transferring** | `p2pd-transfer` | Download in progress with progress bar |
| **complete** | `p2pd-complete` | Download finished |
| **error** | `p2pd-error` | Connection failed — shows fallback link |

---

## 6. Direct-Connect Only — No TURN, Bandwidth, & Error UX

### No TURN relay (design decision)

P2P uses **only STUN hole-punching** — no TURN server. The ICE servers in
`wwwroot/js/p2p/peerConnection.js` are Google STUN endpoints only.

Rationale: a TURN relay carries the **whole file through the server** (roughly
2× the file in bandwidth once in + once out), which defeats the entire point of
P2P — the server would consume bandwidth exactly as if it hosted the file, and
this project already has a normal **R2 upload/download** path for that case.
Self-hosting TURN on a low-bandwidth VPS is not worthwhile.

**Bandwidth effect:**

| Path | Server bandwidth cost |
|------|----------------------|
| P2P direct (STUN hole-punch, works same LAN/low-NAT) | **0** |
| TURN relay (removed) | ~2× file — never used |
| Normal server (R2) transfer | 0 egress (R2 egress free) |

### When direct connection works

Direct P2P connects when both devices can reach each other, i.e.:

- on the **same Wi-Fi / LAN**, or **hotspot** (`192.168.x.x` shared network), or
- joined to a **common mesh VPN** (Tailscale, ZeroTier) so both devices share a
  virtual LAN IP that WebRTC can reach directly.

### Error UX (already implemented)

When ICE cannot connect (`connectionState` stuck, `failed`, `disconnected`), the
UI must say **why** — not hang silently:

- `wwwroot/js/p2p/download.js` — ICE-connect timeout (~12 s) + connection-state
  failure both set an error message:

```text
Direct device-to-device transfer failed: the two devices are not on the same
network (different Wi-Fi, or a NAT/firewall blocks the direct connection). To
use a peer-to-peer, connect both devices to the same Wi-Fi or hotspot, or join
a common mesh VPN (e.g. Tailscale, ZeroTier) and reopen this link; otherwise
ask to use a normal server (R2) transfer instead.
```

  with a **Try again** button.
- `wwwroot/js/p2p/send.js` — sender-side **20 s** receiver-connect timeout stops
  showing `Connecting…` forever; receiver shows its own detailed message.
- There is **no server-side fallback link** for P2P: a P2P share creates a
  `Delivery` row with **no uploaded file** in `CreateP2PShareEndpoint`, so
  `/d/{ShareCode}` has nothing to serve. When direct connect fails the user is
  told to switch to a normal server transfer.

---

## 7. Server Wiring

In `src/QuickShare/Program.cs`:

```csharp
// P2P WebRTC signaling
builder.Services.AddSingleton<P2PConnectionManager>();

// ...

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ...

app.UseMiddleware<P2PSignalingMiddleware>();
```

The middleware runs after WebSocket support is enabled. It inspects every request — if the path starts with `/ws/connect/` and it's a WebSocket upgrade, it handles it; otherwise passes through to the next middleware.

---

## 8. Configuration

| Config Key | Purpose | Default |
|------------|---------|---------|
| `QuickShare:MaxP2PConnections` | Maximum concurrent WebSocket connections | Configured in `QuickShareOptions` |

> **Base URL resolution**: The share link base URL is passed by the Web client (`window.location.origin`) in the `POST /api/shares/p2p` request body as `BaseUrl`. No server-side config needed — the client always knows its own origin.

---

## 9. Security Model

| Concern | Protection |
|---------|-----------|
| **Server never sees file data** | WebRTC data channel is direct peer-to-peer |
| **Eavesdropping** | WebRTC DTLS encrypts all data in transit |
| **Unauthorized access** | Share code is 16-char hex (64-bit random) — unguessable |
| **Share expiry** | Delivery record auto-expires after 7 days (checked on render) |
| **No app-layer encryption** | P2P data channel is plaintext; only WebRTC DTLS protects transit |
| **Connection limit** | `MaxP2PConnections` prevents DoS via connection exhaustion |
| **Room slot protection** | Only one sender per share code; unlimited parallel receivers |

---

## 10. File Reference

| File | Role |
|------|------|
| `src/QuickShare/Features/Shares/P2P/CreateP2PShareEndpoint.cs` | `POST /api/shares/p2p` — share code generation (BaseUrl via client callback) |
| `src/QuickShare/Middleware/P2PConnectionManager.cs` | Room lifecycle, WebSocket tracking, cleanup |
| `src/QuickShare/Middleware/P2PSignalingMiddleware.cs` | WebSocket signaling relay (register, offer, answer, ICE) |
| `src/QuickShare/Program.cs` | WebSocket + middleware wiring |
| `src/QuickShare/Pages/P2PTransfer.razor` | Sender send page UI at `/p2p/send` |
| `src/QuickShare/Pages/P2PDownload.razor` | Receiver download page UI at `/p2p/d/{ShareCode}` |
| `src/QuickShare/wwwroot/js/p2p/send.js` | Sender page orchestrator — file select → share creation → WebRTC → FileSender |
| `src/QuickShare/wwwroot/js/p2p/download.js` | Download page orchestrator — SignalingClient + PeerConnection + FileReceiver |
| `src/QuickShare/wwwroot/js/p2p/signaling.js` | WebSocket signaling client |
| `src/QuickShare/wwwroot/js/p2p/peerConnection.js` | WebRTC RTCPeerConnection wrapper |
| `src/QuickShare/wwwroot/js/p2p/fileTransfer.js` | Chunked file sender with backpressure |
| `src/QuickShare/wwwroot/js/p2p/fileReceiver.js` | Chunked file receiver (blob + StreamSaver) |
| `src/QuickShare/wwwroot/js/p2p/encryption.js` | Legacy PIN encryption module — no longer used (sender passes `null` key) |
