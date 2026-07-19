# Story 1.4: E2E Encryption

- **Epic:** Epic 1 — Core Delivery Pipeline
- **Story ID:** 1.4
- **Status:** ready-for-dev
- **Dependencies:** Story 1.1 ✅, 1.2 ✅, 1.3 ✅

---

## User Story

> As a sender, I want files encrypted on my device before upload,
> So that no one — not ZapDrop, not the storage — can read them.

## Acceptance Criteria

| AC | Description |
|---|---|
| AC-1 | WebCrypto generates AES-256-GCM key; file encrypted before chunking; per-chunk nonce + AAD; only ciphertext uploaded |
| AC-2 | Manifest encrypted with same key; uploaded as ciphertext |
| AC-3 | Link format: `/d/{shareId}#{base64url-key}`; key in URL fragment only |
| AC-4 | Recipient decrypts with key from fragment → decrypt manifest → decrypt chunks with AEAD |
| AC-5 | Link without fragment → "Invalid or incomplete link" |
| AC-6 | Network inspection → all request bodies contain only ciphertext |

## Implementation

### 1. JS Crypto Module (`src/TeleCloud.Web/wwwroot/js/zapdrop-crypto.js`)

Functions:
- `generateKey()` → returns CryptoKey
- `exportKey(key)` → base64url string
- `importKey(base64urlKey)` → CryptoKey
- `encryptChunk(key, plaintext, nonce, aad)` → ciphertext ArrayBuffer
- `decryptChunk(key, ciphertext, nonce, aad)` → plaintext ArrayBuffer
- `encryptManifest(key, manifestJson)` → encrypted bytes + nonce
- `decryptManifest(key, encryptedBytes, nonce)` → manifest JSON
- `generateNonce()` → 96-bit Uint8Array (12 bytes)

### 2. Manifest Format

```json
{
  "fileName": "...",
  "mimeType": "...",
  "fileSize": 12345,
  "chunkSize": 26214400,
  "chunks": [
    { "index": 0, "storageRef": "...", "sha256": "...", "nonce": "base64url" },
    ...
  ],
  "fileSha256": "..."
}
```

### 3. C# Interop (`src/TeleCloud.Web/Services/CryptoInterop.cs`)

IJSRuntime wrappers for the JS module.

---

## Dev Notes

- Encrypt-before-upload (AD-15): AES-256-GCM, per-chunk nonce, AAD = `manifest_id ‖ chunk_index`
- Key in URL fragment only — never sent to server (AD-19)
- Server stores only ciphertext — no encryption/decryption on server
