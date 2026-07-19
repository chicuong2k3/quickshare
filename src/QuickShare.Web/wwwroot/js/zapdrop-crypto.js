// ZapDrop E2E Crypto — WebCrypto API wrapper
// Uses AES-256-GCM with per-chunk 96-bit nonces and AAD binding.
// Key format: raw 256-bit AES key, exported as base64url for URL fragment.
// All operations are client-side only — key never leaves the browser.

const ALGORITHM = { name: 'AES-GCM', length: 256 };
const NONCE_LENGTH = 12; // 96 bits

async function generateKey() {
    return await crypto.subtle.generateKey(ALGORITHM, true, ['encrypt', 'decrypt']);
}

async function exportKey(key) {
    const raw = await crypto.subtle.exportKey('raw', key);
    return btoa(String.fromCharCode(...new Uint8Array(raw)))
        .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function importKey(base64urlKey) {
    const base64 = base64urlKey.replace(/-/g, '+').replace(/_/g, '/');
    const raw = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    return await crypto.subtle.importKey('raw', raw, ALGORITHM, false, ['decrypt']);
}

function generateNonce() {
    return crypto.getRandomValues(new Uint8Array(NONCE_LENGTH));
}

function encodeNonce(nonce) {
    return btoa(String.fromCharCode(...nonce))
        .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function decodeNonce(encoded) {
    const base64 = encoded.replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
}

async function encryptChunk(key, plaintext, nonce, aad) {
    const encodedAad = new TextEncoder().encode(aad);
    return await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv: nonce, additionalData: encodedAad },
        key,
        plaintext
    );
}

async function decryptChunk(key, ciphertext, nonce, aad) {
    const encodedAad = new TextEncoder().encode(aad);
    return await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: nonce, additionalData: encodedAad },
        key,
        ciphertext
    );
}

async function encryptManifest(key, manifestObj) {
    const json = JSON.stringify(manifestObj);
    const plaintext = new TextEncoder().encode(json);
    const nonce = generateNonce();
    const ciphertext = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv: nonce },
        key,
        plaintext
    );
    return { ciphertext: new Uint8Array(ciphertext), nonce: encodeNonce(nonce) };
}

async function decryptManifest(key, ciphertext, nonceEncoded) {
    const nonce = decodeNonce(nonceEncoded);
    const plaintext = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: nonce },
        key,
        ciphertext
    );
    return JSON.parse(new TextDecoder().decode(plaintext));
}

// Expose to Blazor JS interop via DotNet.invokeMethodAsync return values
window.ZapDropCrypto = {
    generateKey,
    exportKey,
    importKey,
    generateNonce,
    encodeNonce,
    decodeNonce,
    encryptChunk,
    decryptChunk,
    encryptManifest,
    decryptManifest,
    NONCE_LENGTH
};
