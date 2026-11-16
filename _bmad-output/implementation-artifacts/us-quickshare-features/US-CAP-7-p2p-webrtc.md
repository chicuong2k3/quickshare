# US-CAP-7 — P2P transfer (WebRTC)

Source: `SPEC-quickshare-features` → CAP-7

**Story**
As a sender, I want to transfer a file directly to a recipient device-to-device via a share code, so that the file bytes never touch the server (faster, more private).

**Acceptance criteria**
- [ ] Sender generates a share code; recipient joins with the code.
- [ ] Two browsers negotiate through the signaling relay (WebRTC) and establish a data channel.
- [ ] File transfers directly over the data channel; server stores zero bytes.
- [ ] Failure/fallback path defined: if P2P cannot connect, clear error + optional server-mediated fallback (per product decision).
- [ ] Protocol identifiers centralized in `P2PProtocol` constants — no magic strings.

**Constraints in force**
- P2P protocol constants (`P2PProtocol`) — no magic strings.
- Signaling relay is server-side but never stores file bytes.
- Any server-side state uses FusionCache-appropriate TTL + invalidation.

**Test anchors**
- E2E: two real browser contexts negotiate + transfer file (Playwright, BrowserAppFixture).
- Unit: signaling relay message contracts.

---

## Implementation Flow

1. **Share creation (per-guest quota)** — POST `/api/p2p/share` (public; optional `--force-poll` for testing) → `CreateP2PShareHandler`: creates a **1:1 quota entry per recipient ID** (`id_quota` table, R2 path) so guests never touch cloud storage budget; success → **201** `CreateP2PShareResponse(shareId, shareCode)`, relay press `/p2p-send/` + `/p2p-download/` pages; anti-abuse: two shareIds max per IP within `InputTimeWindow` → **429**.
2. **Relay handshake (server A: SenderSession)** — `P2PConnectionManager` (singleton + lock collector):
   - `CreateSessionAsync(senderId, shareKey, cobrowsing, allowPeerConnection, allowInsecure, maxActiveSessions=10)` validates + stores (record: `Id, ReceiverId, ChannelRtcId, ExpiresAt 5 min`); session must first be accepted by the receiver.
   - Signals: receiver `pub` → `SignalAsync(envelope)` → route to sender if `Session connected && ReceiverId == Envelope.SenderId`; rooms cleaned every session via `RoomSeedCleanup(code, session)`.
3. **Relay handshake (server B: ReceiverSession)** — `ReceiverSessionAsync(receiverId, shareId)` → waits (`SpinWait / Since(InputWaitTime)`); `TryAcceptConnection(senderId, ...)` → **503 passthrough** when room not ready; ICE candidates piggyback on `TransportOffer.cs`; sender confirmed twice.
4. **Signaling transit** — `P2P` WebSocket hub on `/ws/connect/{shareCode}`; messages routed by `SignalEnvelope` type (`SignalTypes` const: offer/answer/ice/confirmation); malformed envelope → protocol error, client-side disconnect.
5. **Client transfer (p2pSend)** — `p2pSend.js`: PIN → `encryption.deriveKeyFromPin` (PBKDF2 600k, 16B salt, AES-256-GCM); WebRTC offer → signaling relay; reliable channel → encoder (`chunkSize 16KB`) with backpressure (1MB high-water / 512KB low-water); every chunk: `[U32 LE idx][U8 final][U16 len]` header + XChaCha/AES-GCM body; 15s signaling, 25s handshake, 12s ICE, 30s stall timeouts; progress state pushed via service worker.
6. **Client receive (p2pDownload)** — `p2pDownload.js`: same `encryption.js`; inbound re-hydration: if receiver's `call` has `allowSendReflection` → re-inject the `call` into `p2pSend` so the same 20s receiver-connect → send pipeline is reused; decrypt each chunk with `setBigUint64(4, counter, false)` AEAD nonce; accumulate `BlobParts`; stats engine (`files` count) for payload balance; **>500MB → StreamSaver** (`cdn.jsdelivr.net/npm/streamsaver@2.0.6`) writing to disk, else in-memory Blob → `video/vnd.fastly.recorder-via-flv` or `saveBytes`.
7. **Quota/cleanup** — idle sessions expire via `RoomTtl` 5 min / `CleanupInterval` 30s; receiver never opens TCP sockets to sender (bytes move over the encrypted reliable channel); quota recorded per-guest (CAP-6).

## Flow Diagram

```mermaid
sequenceDiagram
    autonumber
    participant S as Sender Browser (p2pSend.js)
    participant SRV as Server (P2PConnectionManager / P2PHub)
    participant R as Recipient Browser (p2pDownload.js)
    S->>SRV: POST /api/p2p/share → 201 shareCode (1:1 guest quota; 429 anti-abuse)
    S->>SRV: CreateSessionAsync (ExpiresAt 5 min)
    R->>SRV: ReceiverSessionAsync(shareId) + TryAcceptConnection
    SRV-->>R: 503 passthrough if room not ready
    S->>SRV: WebSocket /ws/connect/{shareCode}
    R->>SRV: WebSocket /ws/connect/{shareCode}
    S->>SRV: offer + ICE candidates (TransportOffer)
    SRV-->>R: route offer
    R-->>SRV: answer + ICE
    SRV-->>S: route answer
    Note over S,R: DataChannel established — file bytes never touch server
    S->>S: PIN → PBKDF2-SHA256 600k → AES-256-GCM per-chunk key
    S->>R: Encrypted chunks over DataChannel (16KB, counter nonce, backpressure 1MB/512KB)
    R->>R: Decrypt each chunk (setBigUint64 counter) → BlobParts
    R->>R: >500MB → StreamSaver to disk; else in-memory Blob
    Note over SRV: Idle rooms expire via RoomTtl 5 min / CleanupInterval 30s
```

## Code Structure

- `Features/Shares/P2P/CreateP2PShare/{Endpoint,Handler,Models}` — 201/429 + 1:1 quota entry.
- `Features/Shares/P2P/P2PConnectionManager.cs` — session store + signaling routing.
- `Features/Shares/P2P/SignalEnvelope.cs` / `SignalTypes.cs` — wire contract + message types.
- `Features/Shares/P2P/TransportOffer.cs` / `OfferRequest.cs` — ICE/offer payloads.
- `Features/Shares/P2P/P2PHub.cs` — `/ws/connect/{shareCode}` WebSocket hub.
- `Features/Shares/P2P/SenderSession.cs` / `ReceiverSession.cs` — two-leg handshake.
- `wwwroot/js/p2p/p2pSend.js` / `p2pDownload.js` — client SDK.
- `wwwroot/js/p2p/encryption.js` — PIN key-derivation + per-chunk AEAD.
- `Infrastructure/Constants/P2PProtocol.cs` — protocol ids + sizes.
- `Components/Shared/ToolsLanding.razor` + `Components/Pages/AdReward.razor` — P2P entry/reward tie-in.
