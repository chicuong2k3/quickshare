// PIN-based AES-256-GCM encryption for P2P file transfers.
// Uses Web Crypto API with PBKDF2 key derivation.
(function () {
    'use strict';

    var PBKDF2_ITERATIONS = 600000;
    var SALT_LENGTH = 16;
    var KEY_LENGTH = 256;
    var NONCE_LENGTH = 12;

    async function deriveKey(pin, salt) {
        var enc = new TextEncoder();
        var keyMaterial = await crypto.subtle.importKey(
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

    async function encryptChunk(chunk, key, counter) {
        var nonce = new Uint8Array(NONCE_LENGTH);
        var view = new DataView(nonce.buffer);
        view.setBigUint64(4, BigInt(counter), false); // Big-endian, last 8 bytes

        var encrypted = await crypto.subtle.encrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            chunk
        );

        return encrypted;
    }

    async function decryptChunk(chunk, key, counter) {
        var nonce = new Uint8Array(NONCE_LENGTH);
        var view = new DataView(nonce.buffer);
        view.setBigUint64(4, BigInt(counter), false);

        var decrypted = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: nonce },
            key,
            chunk
        );

        return decrypted;
    }

    function generateSalt() {
        var salt = new Uint8Array(SALT_LENGTH);
        crypto.getRandomValues(salt);
        return Array.from(salt, function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    async function hashPin(pin, salt) {
        var enc = new TextEncoder();
        var data = enc.encode(pin + '|' + salt);
        var hash = await crypto.subtle.digest('SHA-256', data);
        return Array.from(new Uint8Array(hash), function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    window.P2P = window.P2P || {};
    window.P2P.encryption = {
        deriveKey: deriveKey,
        encryptChunk: encryptChunk,
        decryptChunk: decryptChunk,
        generateSalt: generateSalt,
        hashPin: hashPin
    };
