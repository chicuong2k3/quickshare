---
id: SPEC-webrtc-p2p
companions:
  - pricing-impact.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# WebRTC P2P Delivery

## Why

Server-based file transfer costs bandwidth, imposes size limits, and creates upload latency for large files. When both sender and receiver are online simultaneously, these transfers can happen browser-to-browser over WebRTC — with no server touching the file data, no size cap, and lower latency. This spec adds WebRTC P2P as an alternative delivery path that activates automatically when the receiver is online, while keeping server-based transfer as the fallback. The P2P path must remain unlimited across all tiers (including Free) to remain competitive with offline-P2P tools like LocalSend. Server infrastructure costs are covered by paid-subscription features and free-tier advertising.

## Capabilities

- **CAP-1**
  - **intent:** Sender and receiver establish a direct WebRTC DataChannel connection when both are online simultaneously, and the file transfers browser-to-browser without touching the server.
  - **success:** A file selected by the sender arrives intact at the receiver via WebRTC DataChannel. The sender's browser never uploads to a server endpoint, and the receiver's browser never downloads from one. The file is bit-identical on both ends.

- **CAP-2**
  - **intent:** The system falls back to existing server-based delivery automatically when P2P connection fails — receiver offline, NAT/firewall blocks WebRTC, or the connection times out.
  - **success:** Sender shares one link. If the receiver opens it while the sender is online, transfer goes P2P. If the receiver opens it later (sender offline) or WebRTC fails, the same link delivers the file via existing server-based chunked download — no manual retry, no second link.

- **CAP-3**
  - **intent:** P2P transfers stream directly to disk with no server-side size limit. Files are read in chunks, the DataChannel respects `bufferedAmount` backpressure, and the receiver reassembles chunks to disk without buffering the entire file in memory.
  - **success:** A 100 GB+ file transfers completely via P2P. The sender's and receiver's browser memory usage stays within predictable bounds regardless of file size (only chunk-sized buffers). No server storage is consumed.

- **CAP-4**
  - **intent:** The sender can optionally set a PIN/password that the receiver must provide before the WebRTC DataChannel decrypts and delivers the file.
  - **success:** When PIN is set, the receiver sees a password prompt before download starts. Without the correct PIN, the encrypted channel never yields decipherable data. Without PIN set, transfer proceeds unencrypted (DTLS transport-layer encryption still applies).

## Constraints

- WebRTC DataChannel only — no media streams (audio/video). Pure binary data transfer.
- Reuse existing WebSocket infrastructure for SDP/ICE signaling. No new dependencies for the signaling layer.
- Chunked streaming with `bufferedAmount` backpressure. The sender must never enqueue more data than the DataChannel can absorb, and the receiver must write chunks to an in-memory buffer or (preferably) the File System Access API / streaming download without loading the full file.
- P2P must be transparent to the end-user's flow: one link, one click. The user should only perceive faster transfer speed, not a different workflow.
- P2P is unlimited in all tiers (Free, Starter, Ultimate). File-size limits from the existing pricing model apply only to server-based fallback transfers.
- Free tier monetized through advertising (wallpaper ads on download pages, as defined in the existing pricing scheme).

## Non-goals

- Not replacing server-based transfer — P2P is a transparent optimization when conditions allow; server remains the fallback.
- Receiver does not upload files to the system — download-only role.
- No native mobile apps — browser-only via Blazor Web.
- No real-time chat or collaboration features — file transfer only.
- No replacement of the existing pricing model — the current tier structure (Free/Starter/Ultimate) is updated to reflect P2P vs server limits, not replaced.

## Success signal

A sender selects a 10 GB file, creates a share link, and sends it to a receiver. Both are online. The file transfers via WebRTC DataChannel — zero bytes hit the server. The same link, when opened by a receiver 2 hours later (sender offline), falls back to the existing server-based download automatically — the receiver gets the file without any extra steps. A free-tier sender successfully sends a 5 GB file via P2P (no server quota consumed) and sees wallpaper ads on the download page. A Starter-tier sender sends a password-protected 5 GB P2P file with no ads.

## Assumptions

- P2P file-size limits in any tier would drive users to free offline-P2P alternatives like LocalSend — P2P must be unlimited across all tiers.
- Current WebSocket signaling infrastructure can be extended with minimal changes to support SDP/ICE message relay.
- Browsers that support WebRTC DataChannel (Chrome, Firefox, Safari, Edge) cover the target user base. Safari's DataChannel support may have limitations; these are acceptable for MVP.

## Open Questions

- **Online detection mechanism**: How does the sender know the receiver is online? Two options: (a) WebSocket presence server — server tracks connected WebSocket clients by user/link and broadcasts online status; (b) share-link polling — receiver opening the link sends a signal, sender receives it. No decision yet.
