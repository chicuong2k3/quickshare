# QuickShareCloud — P2P Transfer Architecture

## Overview

QuickShareCloud supports **direct peer-to-peer file transfers** via WebRTC, bypassing the server entirely for file data. The server acts only as a **signaling relay** — it creates share links, coordinates WebSocket connections between sender and receiver, and relays WebRTC SDP/ICE messages. The file bytes never touch the server.

> Sender → Server (metadata + signaling) → Receiver → WebRTC Data Channel (direct) → File delivered.

---

## 1. Component Map

| Layer | Project | File | Role |
|-------|---------|------|------|
| Share Link Creation | `QuickShare.Server` | `Features/Shares/P2P/CreateP2PShareEndpoint.cs` | `POST /api/shares/p2p` — creates share code + Delivery record |
| Connection Manager | `QuickShare.Server` | `Middleware/P2PConnectionManager.cs` | Room lifecycle, sender/receiver WebSocket tracking, connection limits |
| Signaling Middleware | `QuickShare.Server` | `Middleware/P2PSignalingMiddleware.cs` | WebSocket endpoint `/ws/connect/{shareCode}` — register, relay offer/answer/ICE |
| Server WebSocket Config | `QuickShare.Server` | `Program.cs` | `UseWebSockets` + `P2PSignalingMiddleware` + `P2PConnectionManager` singleton |
| Download Page UI | `QuickShare.Web` | `Pages/P2PDownload.razor` | Receiver page at `/p2p/d/{ShareCode}` |
| Download Page Orchestrator | `QuickShare.Web` | `Pages/P2PDownload.razor.js` | Coordinates SignalingClient + PeerConnection + FileReceiver |
| Signaling Client | `QuickShare.Web` | `wwwroot/js/p2p/signaling.js` | WebSocket connection, auto-reconnect, message relay callbacks |
| Peer Connection | `QuickShare.Web` | `wwwroot/js/p2p/peerConnection.js` | RTCPeerConnection wrapper — offer, answer, ICE candidates, data channels |
| File Sender | `QuickShare.Web` | `wwwroot/js/p2p/fileTransfer.js` | Chunked file send over WebRTC data channel with backpressure |
| File Receiver | `QuickShare.Web` | `wwwroot/js/p2p/fileReceiver.js` | Chunked file receive — blob mode (<500MB) or StreamSaver (>500MB) |
| Encryption | `QuickShare.Web` | `wwwroot/js/p2p/encryption.js` | PIN-based AES-256-GCM via Web Crypto PBKDF2 |

---

## 2. Server Components

### 2.1 P2PConnectionManager

Singleton service managing all active P2P rooms.

```
P2PRoom
├── ShareCode     — unique lookup key (16-char hex)
├── Sender        — WebSocket (null if not connected)
├── Receiver      — WebSocket (null if not connected)
├── FileName      — set by sender during register
├── FileSize      — set by sender during register
├── MimeType      — set by sender during register
├── CreatedAt     — room creation timestamp
├── LastActivity  — bumped on sender/receiver registration
├── SenderConnected    — Sender?.State == WebSocketState.Open
├── ReceiverConnected  — Receiver?.State == WebSocketState.Open
└── IsComplete         — both connected
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
  5. If receiver already connected → notify sender ("peer-joined") + send file-meta to receiver

Receiver registers:
  1. TryRegisterReceiver(shareCode, ws) — fails if receiver already connected
  2. Send { type: "registered", role: "receiver" } back
  3. If sender already connected → notify sender ("peer-joined") + send file-meta to receiver
  4. If sender not connected → send { type: "waiting" }
```

**Disconnect handling:**
- When either peer disconnects, notify the other with `{ type: "peer-left" }`
- Track role per connectionId (shareCode + ws.GetHashCode())

**Relay (offer/answer/ice):**
- Looks up the room, determines the opposite peer, forwards the message verbatim with `{ type, data }`

---

## 3. Client-Side JavaScript Modules

All modules registered under `window.P2P` namespace.

### 3.1 SignalingClient (`window.P2P.SignalingClient`)

- **Constructor**: `(apiBaseUrl, shareCode)`
- **`connect(role, fileMeta)`**: Opens WebSocket to `ws(s)://{apiBase}/ws/connect/{shareCode}`, sends `register` on open
- **Auto-reconnect**: Up to 3 attempts with exponential backoff (1s, 2s, 4s)
- **`send(type, payload)`**: Sends `{ type, data }` JSON message
- **`close()`**: Graceful shutdown

**Inbound message dispatch:**

| Server Message | Callback |
|---------------|----------|
| `registered` | `onRegistered(role)` |
| `peer-joined` | `onPeerJoined()` |
| `file-meta` | `onFileMeta({ fileName, fileSize, mimeType })` |
| `waiting` | `onWaiting(message)` |
| `peer-left` | `onPeerLeft(message)` |
| `error` | `onError(message)` |
| `offer` / `answer` / `ice` | `onRelay(type, data)` |

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
- **Flow**: Send JSON metadata → read file in chunks → encrypt (if PIN) → prepend header → send over data channel

### 3.4 FileReceiver (`window.P2P.FileReceiver`)

Receiver-side chunked download from the data channel.

- **Blob mode** (<500MB): Accumulates chunks in memory, downloads via blob URL
- **StreamSaver mode** (>500MB): Lazy-loads StreamSaver.js from CDN, streams chunks to disk
- **Chunk reordering**: Each chunk self-identifies via header index — handles out-of-order arrival
- **Header parsing**: Reads chunk index, final flag, and data length from 8-byte header

### 3.5 Encryption (`window.P2P.encryption`)

End-to-end encryption using Web Crypto API.

- **Key derivation**: PBKDF2 with 600,000 iterations, SHA-256, 256-bit key
- **Salt**: `PIN + shareCode` concatenation (ensures different keys per share even with same PIN)
- **Cipher**: AES-256-GCM, 12-byte nonce with chunk counter in last 8 bytes (prevents nonce reuse)
- **`encryptChunk(chunk, key, counter)`**: Encrypts a single chunk
- **`decryptChunk(chunk, key, counter)`**: Decrypts a single chunk

---

## 4. Full P2P Transfer Flow

### 4.1 Share Link Creation (Sender)

```
Sender                          Server
  │                                │
  │  POST /api/shares/p2p          │
  │  { fileName, fileSize,         │
  │    mimeType, hasPin }          │
  │───────────────────────────────▶│
  │                                │── Generate shareCode (16 hex chars)
  │                                │── Create Delivery record (ExpiresAt = +7 days)
  │                                │
  │◀───────────────────────────────│
  │  201 Created                   │
  │  { shareCode, shareLink }      │
  │  shareLink = /p2p/d/{code}     │
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
  │  ║  Encrypts (if PIN) → header + chunk → DC    ║        │
  │  ║══════════════════════════════════════════════╣        │
  │  ║  Receiver parses header → decrypts → blob   ║        │
  │  ╚══════════════════════════════════════════════╝        │
  │                         │                                │
  │                         │  File downloaded.              │
  │                         │  Server never saw the data.    │
  │                         │                                │
```

### 4.3 PIN-Protected Transfer (Optional)

```
Receiver Browser

  Page shows PIN input
  User enters PIN
  ─────────────────
  1. deriveKey(pin, salt = pin + shareCode)
     → PBKDF2 600K iterations → AES-256-GCM key
  2. Sender encrypts each chunk before sending:
     encryptChunk(chunk, key, chunkIndex)
  3. Receiver decrypts each chunk on arrival:
     decryptChunk(chunk, key, chunkIndex)
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
| **pin-required** | `p2pd-pin` | File has PIN, user must enter it |
| **ready** | `p2pd-ready` | File metadata received, ready to download |
| **connecting** | `p2pd-ready` (btn disabled) | WebRTC handshake in progress |
| **transferring** | `p2pd-transfer` | Download in progress with progress bar |
| **complete** | `p2pd-complete` | Download finished |
| **error** | `p2pd-error` | Connection failed — shows fallback link |

---

## 6. Server Wiring

In `src/QuickShare.Server/Program.cs`:

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

## 7. Configuration

| Config Key | Purpose | Default |
|------------|---------|---------|
| `QuickShare:P2PBaseUrl` | Base URL for generated share links (optional) | Scheme + Host from request |
| `QuickShare:MaxP2PConnections` | Maximum concurrent WebSocket connections | Configured in `QuickShareOptions` |

---

## 8. Security Model

| Concern | Protection |
|---------|-----------|
| **Server never sees file data** | WebRTC data channel is direct peer-to-peer |
| **Eavesdropping** | WebRTC DTLS encrypts all data in transit |
| **Unauthorized access** | Share code is 16-char hex (64-bit random) — unguessable |
| **Share expiry** | Delivery record auto-expires after 7 days (checked on render) |
| **Optional PIN protection** | AES-256-GCM with PBKDF2 (600K iterations) — even server can't decrypt |
| **Connection limit** | `MaxP2PConnections` prevents DoS via connection exhaustion |
| **Room slot protection** | Only one sender + one receiver per share code |

---

## 9. File Reference

| File | Role |
|------|------|
| `src/QuickShare.Server/Features/Shares/P2P/CreateP2PShareEndpoint.cs` | `POST /api/shares/p2p` — share code generation |
| `src/QuickShare.Server/Middleware/P2PConnectionManager.cs` | Room lifecycle, WebSocket tracking, cleanup |
| `src/QuickShare.Server/Middleware/P2PSignalingMiddleware.cs` | WebSocket signaling relay (register, offer, answer, ICE) |
| `src/QuickShare.Server/Program.cs` | WebSocket + middleware wiring |
| `src/QuickShare.Web/Pages/P2PDownload.razor` | Receiver download page UI |
| `src/QuickShare.Web/Pages/P2PDownload.razor.js` | Download page state machine / orchestrator |
| `src/QuickShare.Web/wwwroot/js/p2p/signaling.js` | WebSocket signaling client |
| `src/QuickShare.Web/wwwroot/js/p2p/peerConnection.js` | WebRTC RTCPeerConnection wrapper |
| `src/QuickShare.Web/wwwroot/js/p2p/fileTransfer.js` | Chunked file sender with backpressure |
| `src/QuickShare.Web/wwwroot/js/p2p/fileReceiver.js` | Chunked file receiver (blob + StreamSaver) |
| `src/QuickShare.Web/wwwroot/js/p2p/encryption.js` | PIN-based AES-256-GCM encryption |
