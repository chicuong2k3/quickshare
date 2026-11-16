// PIN-based AES-256-GCM encryption for P2P file transfers.
// Uses Web Crypto API with PBKDF2 key derivation.
(function () {
    'use strict';

    const PBKDF2_ITERATIONS = 600000;
    const SALT_LENGTH = 16;
    const KEY_LENGTH = 256;
    const NONCE_LENGTH = 12;

    async function deriveKey(pin: string, salt: string): Promise<CryptoKey> {
        const enc = new TextEncoder();
        const keyMaterial = await crypto.subtle.importKey(
            'raw',
            enc.encode(pin),
            { name: 'PBKDF2' },
            false,
            ['deriveKey']
        );

        return crypto.subtle.deriveKey(
            {
                name: 'PBKDF2',
                salt: enc.encode(salt),
                iterations: PBKDF2_ITERATIONS,
                hash: 'SHA-256'
            },
            keyMaterial,
            { name: 'AES-GCM', length: KEY_LENGTH },
            false,
            ['encrypt', 'decrypt']
        );
    }

    async function encryptChunk(chunk: ArrayBuffer | Uint8Array, key: CryptoKey, counter: number): Promise<ArrayBuffer> {
        const nonce = new Uint8Array(NONCE_LENGTH);
        const view = new DataView(nonce.buffer);
        view.setBigUint64(4, BigInt(counter), false); // Big-endian, last 8 bytes

        const encrypted = await crypto.subtle.encrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            chunk as BufferSource
        );

        return encrypted;
    }

    async function decryptChunk(chunk: ArrayBuffer | Uint8Array, key: CryptoKey, counter: number): Promise<ArrayBuffer> {
        const nonce = new Uint8Array(NONCE_LENGTH);
        const view = new DataView(nonce.buffer);
        view.setBigUint64(4, BigInt(counter), false);

        const decrypted = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            chunk as BufferSource
        );

        return decrypted;
    }

    function generateSalt(): string {
        const salt = new Uint8Array(SALT_LENGTH);
        crypto.getRandomValues(salt);
        return Array.from(salt, function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    async function hashPin(pin: string, salt: string): Promise<string> {
        const enc = new TextEncoder();
        const data = enc.encode(pin + '|' + salt);
        const hash = await crypto.subtle.digest('SHA-256', data);
        return Array.from(new Uint8Array(hash), function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    window.P2P = window.P2P || ({} as P2PNamespace);
    window.P2P.encryption = {
        deriveKey: deriveKey,
        encryptChunk: encryptChunk,
        decryptChunk: decryptChunk,
        generateSalt: generateSalt,
        hashPin: hashPin
    };
})();