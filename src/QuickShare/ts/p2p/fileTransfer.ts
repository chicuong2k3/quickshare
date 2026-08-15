// Chunked file sender over WebRTC data channel with backpressure.
(function () {
    'use strict';

    const CHUNK_SIZE = 16 * 1024; // 16KB
    const BACKPRESSURE_HIGH = 1024 * 1024; // 1MB
    const BACKPRESSURE_LOW = 512 * 1024; // 512KB

    class FileSender implements P2PFileSender {
        file: File;
        dc: RTCDataChannel;
        encryption: P2PEncryptionAdapter | null;
        _cancelled: boolean;

        // Event callbacks
        onProgress: ((sent: number, total: number) => void) | null;
        onComplete: (() => void) | null;
        onError: ((message: string) => void) | null;

        constructor(file: File, dataChannel: RTCDataChannel, encryption: P2PEncryptionAdapter | null) {
            this.file = file;
            this.dc = dataChannel;
            this.encryption = encryption || null;
            this._cancelled = false;

            // Event callbacks
            this.onProgress = null;
            this.onComplete = null;
            this.onError = null;
        }

        start(): void {
            const self = this;
            this._cancelled = false;
            const totalChunks = Math.ceil(this.file.size / CHUNK_SIZE);
            let currentChunk = 0;

            // Send meta first
            try {
                const metaMsg = JSON.stringify({
                    type: 'meta',
                    name: this.file.name,
                    size: this.file.size,
                    mime: this.file.type || 'application/octet-stream',
                    totalChunks: totalChunks
                });
                this.dc.send(metaMsg);
            } catch (e) {
                if (this.onError) this.onError('Failed to send metadata: ' + (e as Error).message);
                return;
            }

            const reader = new FileReader();
            let offset = 0;

            const sendNext = function () {
                if (self._cancelled) return;

                if (offset >= self.file.size) {
                    if (self.onComplete) self.onComplete();
                    return;
                }

                // Backpressure check
                if (self.dc.bufferedAmount > BACKPRESSURE_HIGH) {
                    self.dc.addEventListener('bufferedamountlow', function handler() {
                        self.dc.removeEventListener('bufferedamountlow', handler);
                        sendNext();
                    }, { once: true });
                    self.dc.bufferedAmountLowThreshold = BACKPRESSURE_LOW;
                    return;
                }

                const chunk = self.file.slice(offset, offset + CHUNK_SIZE);
                reader.onload = async function (e: ProgressEvent<FileReader>) {
                    if (self._cancelled) return;

                    const arrayBuffer = (e.target as FileReader).result as ArrayBuffer;
                    const isFinal = (offset + CHUNK_SIZE) >= self.file.size;

                    try {
                        let data: ArrayBuffer = arrayBuffer;

                        // Encrypt if encryption is configured
                        if (self.encryption && self.encryption.key) {
                            data = await self.encryption.encrypt(data, self.encryption.key, currentChunk);
                        }

                        // Create message: first 4 bytes = chunk index as Uint32, rest = chunk data
                        const header = new ArrayBuffer(8);
                        const headerView = new DataView(header);
                        headerView.setUint32(0, currentChunk, true); // chunk index
                        headerView.setUint8(4, isFinal ? 1 : 0); // final flag
                        headerView.setUint16(5, data.byteLength, true); // data length (max 64KB + overhead)

                        const combined = new Uint8Array(header.byteLength + data.byteLength);
                        combined.set(new Uint8Array(header), 0);
                        combined.set(new Uint8Array(data), header.byteLength);

                        self.dc.send(combined.buffer as ArrayBuffer);

                        currentChunk++;
                        offset += CHUNK_SIZE;

                        if (self.onProgress) {
                            self.onProgress(Math.min(offset, self.file.size), self.file.size);
                        }

                        setTimeout(sendNext, 0);
                    } catch (err) {
                        if (self.onError) self.onError('Send failed at chunk ' + currentChunk + ': ' + (err as Error).message);
                    }
                };

                reader.onerror = function () {
                    if (self.onError) self.onError('Failed to read file at chunk ' + currentChunk);
                };

                reader.readAsArrayBuffer(chunk);
            };

            sendNext();
        }

        cancel(): void {
            this._cancelled = true;
        }
    }

    window.P2P = window.P2P || ({} as P2PNamespace);
    window.P2P.FileSender = FileSender;
})();