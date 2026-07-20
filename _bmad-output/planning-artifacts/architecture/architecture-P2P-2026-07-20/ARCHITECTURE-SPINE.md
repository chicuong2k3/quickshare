---
name: TeleCloud WebRTC P2P Delivery
type: architecture-spine
purpose: build-substrate
altitude: feature
paradigm: Blazor SSR + FastEndpoints + Wolverine with WebRTC P2P delivery channel
scope: WebRTC P2P delivery path for QuickShare — signaling, online detection, DataChannel transfer, PIN encryption, server fallback
status: draft
created: 2026-07-20
updated: 2026-07-20 (review iteration)
binds: [CAP-1, CAP-2, CAP-3, CAP-4]
sources: ["_bmad-output/specs/spec-webrtc-p2p/SPEC.md"]
companions: ["_bmad-output/specs/spec-webrtc-p2p/pricing-impact.md"]
parent: "_bmad-output/architecture/architecture-TeleCloud-owned-storage-2026-07-19/ARCHITECTURE-SPINE.md"
---

# Architecture Spine — WebRTC P2P Delivery

## Design Paradigm

**Blazor SSR + FastEndpoints + Wolverine with WebRTC P2P delivery channel.**

Inherits the existing two-process architecture and adds a real-time signaling layer:

- **TeleCloud.Web** — Blazor Static SSR frontend + Alpine.js (existing). New: JavaScript WebRTC client for peer connection, DataChannel I/O, file chunking.
- **TeleCloud.Server** — FastEndpoints API + Wolverine command bus (existing). New: WebSocket middleware for SDP/ICE signaling. No changes to `IStorageProvider` or chunk storage.
- **PostgreSQL** — Metadata unchanged (P2P transfers never touch the database). Optional analytics logging for completed P2P deliveries.
- **No new server infrastructure** — the P2P signaling WebSocket is lightweight and runs in the existing server process.

```mermaid
flowchart LR
    Sender[Browser Sender] -->|HTTPS| Web[TeleCloud.Web<br/>Blazor SSR]
    Sender -->|WebSocket| Signal[Signaling WS<br/>TeleCloud.Server]
    Receiver[Browser Receiver] -->|HTTPS| Web
    Receiver -->|WebSocket| Signal
    Sender ==|WebRTC DataChannel| Receiver

    Signal -->|Online check| PG[("In-memory<br/>Connection Map")]

    style Sender fill:#1a1a2e,stroke:#26ccff,color:#fff
    style Receiver fill:#1a1a2e,stroke:#88ff5a,color:#fff
    style Signal fill:#16213e,stroke:#5E6AD2,color:#fff
    style Web fill:#16213e,stroke:#5E6AD2,color:#fff
```

## Inherited Invariants (from parent system spine)

The following parent ADs are fully binding for the server-fallback path. The P2P path does not override them — it simply takes a different route that doesn't engage these:

| Inherited | From parent | Binds here |
|-----------|-------------|------------|
| AD-1 — IStorageProvider | owned-storage spine | Server fallback uploads/downloads continue using `IStorageProvider`. P2P path never calls it. |
| AD-2 — Chunked Upload Flow | owned-storage spine | Server fallback chunk upload unchanged. P2P has its own chunking (AD-4). |
| AD-6 — Entity Relationship Model | owned-storage spine | Delivery/Manifest/Chunk entities unchanged for server path. P2P deliveries are ephemeral (no DB entities). |
| AD-15 — Client Encryption | owned-storage spine | Server-bound files still encrypt-before-upload with fragment key. P2P uses a different key path (PIN-derived, AD-5). |
| AD-16 — Download Count Cap | owned-storage spine | Server fallback enforces `MaxDownloads`. P2P has no download cap (sender controls availability by staying online). |
| AD-17 — Egress Accounting | owned-storage spine | Server fallback tracks egress. P2P has zero egress — no accounting needed. |
| AD-18 — Ad Injection | owned-storage spine | P2P download page still shows ads for free tier via same ad-injection mechanism. |
| AD-19 — Access Control | owned-storage spine | Server fallback uses password + fragment key. P2P PIN encryption is independent (AD-5). |
| AD-21 — Anti-Adblock | owned-storage spine | Applies identically to P2P download page. |

## Invariants & Rules

### AD-1 — P2P-First with Upload-on-Demand Fallback

- **Binds:** CAP-1, CAP-2
- **Prevents:** Wasteful pre-uploads; dual-link confusion; sender managing two separate share flows
- **Rule:** A single share link serves both P2P and server fallback. Sender creates the link while connected to the signaling WebSocket and keeps the browser tab open (background allowed). When receiver opens the link:

  - **Sender online, P2P succeeds** → DataChannel transfer. Zero server storage consumed.
  - **Sender online, P2P fails** (NAT/firewall blocks WebRTC, connection times out) → server notifies sender via WebSocket → sender sees an in-page upload prompt → sender uploads (existing chunked upload flow) → receiver downloads via server.
  - **Sender already disconnected** → No fallback. Receiver sees "Sender is offline — ask them to come back online to share."

  The receiver-side page decides which path based on sender presence — never asks the user to choose.

- **P2P timeout definition:** WebRTC ICE gathering + connection attempt: 30s timeout. DataChannel ready: 10s after ICE connected. Exceeded → report P2P failure to server → trigger upload-on-demand flow.
- **Sender UI state machine (tied to Alpine.js `x-data`):** `idle` → selecting file → `link-created` (WebSocket open, waiting for peer) → `peer-joined` → `transferring` (P2P or upload) → `complete` / `failed`. States transition via WebSocket messages from the server.

### AD-1a — P2P Share-Link Creation Flow

- **Binds:** CAP-1, CAP-2
- **Prevents:** Upload required before link creation; sender confusion about when the link exists
- **Rule:** P2P share-link creation does NOT require uploading the file first. The flow:
  1. Sender selects file via `<input type="file">` (browser reads metadata — name, size, mime — but NOT the full file into memory)
  2. Sender sets optional PIN, optional access controls
  3. Sender clicks "Create Share Link" → POST to new API endpoint `POST /api/shares/p2p` with file metadata (name, size, mime, PIN-hint), NOT file bytes
  4. Server creates a `ShareLink` record with `{"type":"p2p", "metadata":{name, size, mime}}`, returns the share URL
  5. Sender's browser opens WebSocket to `/ws/connect/{shareCode}` with role `sender`
  6. Server marks share as `online`, waits for receiver
  7. Sender enters `link-created` state, displays the share link URL

  This is a new code path in `ShareController.cs`/`ShareEndpoints.cs` — does NOT modify the existing `POST /api/upload` endpoint.

### AD-2 — Signaling via Raw WebSocket

- **Binds:** CAP-1, signaling layer
- **Prevents:** SignalR dependency bloat; unnecessary reconnection complexity for simple SDP/ICE relay
- **Rule:** ASP.NET Core raw WebSocket middleware (`app.UseWebSockets()` + middleware for `/ws/connect/{shareCode}`). Messages are JSON with discriminated `type` field (register, peer-joined, file-meta, offer, answer, ice, peer-left). No SignalR hub. Server is a transparent message relay — never inspects or stores SDP/ICE payloads.

  - `register` message MUST include `role` (`"sender"` or `"receiver"`) — the server uses this to route messages to the correct peer.
  - Sender sends `fileMeta` (name, size, mime) in `register`. Server stores it ephemerally (in-memory, same TTL as connection map).
  - When receiver connects, server immediately responds with `file-meta` containing the file info and `hasPin` flag (so the receiver's UI can show file name/size and a PIN prompt before WebRTC starts).
  - `peer-joined` is sent to the sender only. `file-meta` is sent to the receiver only.

**Protocol:**

```json
// Sender connects (registers as sender for this shareCode)
{"type": "register", "shareCode": "abc123", "role": "sender", "fileMeta": {"name": "video.mp4", "size": 1073741824, "mime": "video/mp4"}}

// Receiver connects → server stores receiver connection, sends file metadata
//   → server notifies sender
{"type": "peer-joined", "shareCode": "abc123"}

//   → server sends file metadata to receiver immediately
{"type": "file-meta", "shareCode": "abc123", "name": "video.mp4", "size": 1073741824, "mime": "video/mp4", "hasPin": false}

// SDP relay (server transparent-relays each message to the other peer)
{"type": "offer", "sdp": "..."}
{"type": "answer", "sdp": "..."}

// ICE relay
{"type": "ice", "candidate": "..."}

// Disconnect
{"type": "peer-left", "shareCode": "abc123"}
```

### AD-3 — In-Memory Connection Map for Presence

- **Binds:** CAP-1, online detection
- **Prevents:** Database round-trips for ephemeral connection state; polling overhead
- **Rule:** Server maintains an in-memory connection registry with two tiers:
  - `ConcurrentDictionary<string, P2PRoom>` keyed by shareCode. Each `P2PRoom` holds at most 1 sender WebSocket + 1 receiver WebSocket (1:1 model).
  - A lightweight rate-limiting counter (e.g. sliding window) at the server level to cap total concurrent P2P WebSocket connections (default: 1000, configurable via `appsettings.json`).

  **Connection lifecycle:**
  1. First `register {role:"sender"}` creates the `P2PRoom` for that shareCode. Rejects if a sender is already registered.
  2. First `register {role:"receiver"}` joins the room. Rejects if a receiver is already present.
  3. When receiver joins → sender gets `peer-joined`, receiver gets `file-meta`.
  4. Either peer disconnects → `peer-left` sent to the other. If sender disconnects, room is marked `orphaned` and cleaned up after TTL (5 min default).
  5. Room is removed from dictionary when both peers disconnect.

  **No persistence** — connection state is ephemeral. If the server restarts, all active P2P connections are lost (sender's WebSocket onClose triggers UI to show "Server disconnected — please re-create the link").

  **WebSocket endpoint:**
  ```
  GET /ws/connect/{shareCode}
  ```

### AD-4 — Chunked DataChannel with Backpressure + StreamSaver

- **Binds:** CAP-3
- **Prevents:** Sender OOM from unbuffered sends; receiver OOM from full-file buffering; event-loop blocking during large transfers
- **Rule:**
  - Chunk size: 16 KB (within WebRTC SCTP recommended message size)
  - Sender monitors `RTCDataChannel.bufferedAmount`
  - When `bufferedAmount > 1 MB`, sender pauses reading/queuing next chunk
  - On `bufferedamountlow` event (threshold = 512 KB), resume sending
  - **Receiver MUST use StreamSaver.js** (or an equivalent disk-streaming mechanism) for file sizes > 500 MB — chunks are piped to disk via the writable stream, never fully buffered in memory. For files ≤ 500 MB, `new Blob([...parts])` + `<a download>` may be used as a lightweight path.
  - StreamSaver integration: receiver passes each chunk to `WritableStream.write()` (StreamSaver provides the write stream via ServiceWorker). This keeps the receiver's memory footprint at ~chunk size regardless of total file size, satisfying CAP-3 (100 GB+).

```javascript
// Sender flow
const CHUNK_SIZE = 16 * 1024;  // 16KB
const BACKPRESSURE_HIGH = 1_048_576;  // 1MB
const BACKPRESSURE_LOW = 524_288;     // 512KB

dc.bufferedAmountLowThreshold = BACKPRESSURE_LOW;

async function sendFile(file, dc) {
    let offset = 0;
    while (offset < file.size) {
        if (dc.bufferedAmount > BACKPRESSURE_HIGH) {
            await new Promise(r => dc.onbufferedamountlow = r);
        }
        const chunk = await readSlice(file, offset, CHUNK_SIZE);
        dc.send(chunk);
        offset += CHUNK_SIZE;
    }
}
```

### AD-5 — PIN-Based E2E Encryption for P2P

- **Binds:** CAP-4
- **Prevents:** PIN-in-transit leakage; server-side PIN verification (creates liability); PIN ↔ fragment key confusion
- **Rule:** When sender enables PIN protection:
  1. PIN is provided by sender at share-creation time
  2. AES-256-GCM key is derived via PBKDF2(PIN, salt=SHA256(shareCode), iterations=600000, keyLen=256)
  3. Sender encrypts each chunk before sending over DataChannel (AEAD, independent nonce per chunk)
  4. PIN is **never sent** to the server or over any network channel
  5. Receiver must enter the PIN to derive the same key and decrypt
  6. Without PIN set: DTLS transport-layer encryption only (WebRTC default)

This extends the parent AD-15 zero-knowledge philosophy with a different key-delivery mechanism: PIN entry instead of URL fragment.

- **Key separation (P2P PIN vs server fragment key):** These are independent encryption layers and never shared:
  - **P2P path** → PIN-derived key (this AD). Only protects the WebRTC DataChannel.
  - **Server fallback path** → existing AD-15 fragment-key encryption. If P2P fails and upload-on-demand kicks in, the sender uploads the file encrypted with the **fragment key** (AD-15), NOT the PIN key. The fragment key is delivered via URL fragment (`#key`) per AD-15.
  
  This means: a file protected by a PIN during P2P transfer is stored on the server (if fallback activated) with fragment-key encryption — two independent encryption layers for two independent delivery paths. The receiver receives either the PIN (to decrypt P2P) or the fragment key (to decrypt server download), never both for the same transfer.

### AD-6 — P2P Bypasses IStorageProvider Entirely

- **Binds:** CAP-1, CAP-3, cost model
- **Prevents:** Unnecessary storage egress; storage provider coupling in the P2P code path
- **Rule:** The P2P path never calls `IStorageProvider`. Files flow directly from sender's File API → DataChannel → receiver's Blob/StreamSaver. Zero server storage consumed. Zero server egress cost. Optional non-blocking analytics notification sent via WebSocket after P2P completion.

### AD-7 — File Access via File API with Chunked Reader

- **Binds:** CAP-1, CAP-3
- **Prevents:** File System Access API cross-browser complexity; vendor lock-in
- **Rule:** Sender selects file via standard `<input type="file">`. File is read in 16 KB slices using `FileReader.readAsArrayBuffer()`. Sender must keep browser tab open for the full transfer duration. A connection-loss recovery mechanism (same shareCode, resend from last confirmed chunk) is deferred.

```javascript
function readSlice(file, offset, length) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = e => resolve(e.target.result);
        reader.onerror = reject;
        reader.readAsArrayBuffer(file.slice(offset, offset + length));
    });
}
```

## Consistency Conventions

| Concern | Convention |
|---------|-----------|
| Signaling messages | JSON with `type` discriminator field. Server transparent-relay (no inspection). |
| Chunk metadata | In-band: sender sends a header message `{type:"meta", name, size, mime}` before data chunks. Last chunk flag: `{type:"chunk", index, final:true}`. |
| Error propagation | P2P errors surfaced via DataChannel message `{type:"error", code, message}`. Server-side SDP failures relayed via signaling. |
| UI state | Alpine.js handles WebSocket/WebRTC connection state on sender/receiver pages. `x-data="{ status: 'connecting' }"` pattern. |

## Stack

| Name | Version | Purpose |
|------|---------|---------|
| ASP.NET Core WebSocket Middleware | Built-in (.NET 10) | Signaling relay |
| WebRTC DataChannel | Browser API | P2P file transfer |
| AES-256-GCM + PBKDF2 | Web Crypto API | PIN-based encryption |
| StreamSaver.js | 2.x | Mandatory disk-streaming for files > 500 MB on receiver |
| Alpine.js | 3.15.12 (existing) | Reactive UI for connection states |

## Structural Seed

```text
src/
  QuickShare.Server/
    Middleware/
      P2PSignalingMiddleware.cs    # WebSocket middleware for SDP/ICE relay
      P2PConnectionManager.cs     # In-memory ConcurrentDictionary for connection tracking
  QuickShare.Web/
    js/
      p2p/
        signaling.js              # WebSocket client (connect, send, relay messages)
        peerConnection.js          # RTCPeerConnection + DataChannel setup
        fileTransfer.js            # Chunked reader, sender logic
        fileReceiver.js            # Chunk reassembly, StreamSaver integration
        encryption.js              # AES-256-GCM + PBKDF2 PIN encryption
    Models/
      P2PConnectionState.cs        # Blazor model for signaling/WebRTC state
    Pages/
      Send/
        P2PTransfer.razor          # Sender P2P transfer page (replaces upload for P2P shares)
      Receive/
        P2PDownload.razor          # Receiver P2P download page
```

## Capability → Architecture Map

| Capability | Lives in | Governed by |
|------------|----------|-------------|
| CAP-1 (P2P DataChannel) | `p2p/peerConnection.js`, `p2p/signaling.js`, `P2PSignalingMiddleware.cs` | AD-1, AD-1a, AD-2, AD-3, AD-7 |
| CAP-2 (Fallback) | Existing `DownloadDeliveryHandler.cs` + `P2PDownload.razor` (online check) | AD-1 |
| CAP-3 (No size limit) | `p2p/fileTransfer.js` | AD-4, AD-7 |
| CAP-4 (PIN encryption) | `p2p/encryption.js` | AD-5 |

## Deferred

- **DataChannel chunk size:** 16 KB starting default. Optimal size for Vietnamese network conditions requires production benchmarking.
- **STUN/TURN server:** Google public STUN (`stun:stun.l.google.com:19302`) for initial deployment. Self-hosted TURN or third-party TURN (Twilio, Metered) deferred until NAT traversal failures are measured.
- **P2P transfer concurrency limits:** Per-tier concurrency limits (Free: 1, Starter: 2, Ultimate: unlimited) deferred to implementation — architecture supports rate-limiting at signaling layer but specific values not invariant.
- **Connection recovery:** If sender loses connection mid-transfer, resuming from last confirmed chunk. Requires receiver to ACK chunks. Deferred to post-MVP.
- **Multi-receiver support (1 sender → N receivers):** Current AD-3 enforces 1:1. Broadcasting the same file to multiple receivers via separate DataChannels is deferred — requires significant changes to room model and sender-side streaming logic.
- **P2P analytics logging:** Optional non-blocking analytics (transfer count, volume) for business intelligence. Not required for MVP.
