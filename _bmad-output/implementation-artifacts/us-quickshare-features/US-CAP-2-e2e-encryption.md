# US-CAP-2 — End-to-end encryption (zero-knowledge)

Source: `SPEC-quickshare-features` → CAP-2

**Story**
As a sender, I want my uploaded file to be encrypted in my browser before it ever reaches the server, so that nobody with server or database access can read my content.

**Acceptance criteria**
- [ ] Every delivery is encrypted with AES-256-GCM (per-chunk AEAD) in the browser before upload.
- [ ] The key lives only in the URL fragment — never sent to the server, never stored in DB.
- [ ] AAD binds the manifest (metadata integrity).
- [ ] Server and DB contain only ciphertext; server is zero-knowledge.
- [ ] Recipient download in a fresh browser reconstructs a byte-identical file from the fragment key.
- [ ] Corrupted ciphertext fails closed (clear error, no partial garbage output).
- [ ] No plaintext upload toggle exists.

**Constraints in force**
- Key = fragment only; AAD = manifest binding (SPEC constraint).
- Download page no-JS default phase must stay usable (`DownloadNoScriptTests` guard).

**Test anchors**
- Unit: encrypt/decrypt roundtrip byte-identical; tampered chunk fails closed.
- E2E: fresh-browser download from fragment key reproduces original bytes.

---

## Implementation Flow

1. **Browser-side encrypt (upload path)** — `upload.js`: `crypto.subtle.generateKey({AES-GCM, 256})` (non-exportable while encrypting); random 12-byte nonce; whole file read + encrypted in memory → ciphertext blob = `[nonce 12B][ciphertext]` (AES-GCM appends its own 16-byte tag). Raw key exported → `base64url` → carried only in the share-link fragment (`#key=...`).
2. **Zero-knowledge server** — only the ciphertext blob traverses `/api/uploads/start-r2` + `/api/uploads/commit-r2`; key never appears in requests, DB, logs, or R2 metadata. `CommitR2Handler` accounts for the envelope via `EncryptionOverheadSlack = 1024` (already in `TotalSize = partSizes + slack`).
3. **Browser-side decrypt (download path)** — `download.js`: share link containing `#key=` → `isEncrypted`; `fetchBytes` → split nonce (first 12B) → AES-GCM decrypt → `saveBytes` (Blob + `<a download>`). Text preview: media type `text/plain` + size ≤1MB → decrypt then render via `textContent` (50-line truncation at >10KB) — always escaping.
4. **Fail-closed** — WebCrypto decrypt throws on corrupted/tampered ciphertext → error phase (no partial output). No plaintext toggle exists anywhere — encryption is unconditional on the cloud path.
5. **P2P variant (hand-off to CAP-7)** — `p2p/encryption.js` uses the *same* AES-256-GCM + AEAD semantics but per-chunk: PIN-derived key (PBKDF2-SHA256 600k, 16B salt), nonce = 12B with a big-endian `setBigUint64(4, counter)` counter — because the P2P path has no URL fragment to ship a key.

**Divergence note (vs AC)**: AC specifies **per-chunk AEAD with AAD binding the manifest**; production cloud path encrypts the **whole file as a single AES-GCM envelope** (`[nonce][ciphertext]`) with no separate manifest/AAD — simpler, equally zero-knowledge. Per-chunk AEAD exists only in the P2P implementation.

## Code Structure

- `wwwroot/js/upload.js` — encrypt-whole-file, key → fragment.
- `wwwroot/js/download.js` — fragment-key detect, decrypt, save, text preview.
- `wwwroot/js/p2p/encryption.js` — PIN/PBKDF2 per-chunk variant (P2P only).
- `Features/Uploads/CommitR2/CommitR2Handler.cs` — `EncryptionOverheadSlack` accounting.
- `Features/Downloads/Download/DownloadDeliveryHandler.cs` — serves ciphertext bytes as-is.
