// Chunked file sender over WebRTC data channel with backpressure.
(function () {
    'use strict';

    var CHUNK_SIZE = 16 * 1024; // 16KB
    var BACKPRESSURE_HIGH = 1024 * 1024; // 1MB
    var BACKPRESSURE_LOW = 512 * 1024; // 512KB

    function FileSender(file, dataChannel, encryption) {
        this.file = file;
        this.dc = dataChannel;
        this.encryption = encryption || null;
        this._cancelled = false;

        // Event callbacks
        this.onProgress = null;
        this.onComplete = null;
        this.onError = null;
    }

    FileSender.prototype.start = function () {
        var self = this;
        this._cancelled = false;
        var totalChunks = Math.ceil(this.file.size / CHUNK_SIZE);
        var currentChunk = 0;

        // Send meta first
        try {
            var metaMsg = JSON.stringify({
                type: 'meta',
                name: this.file.name,
                size: this.file.size,
                mime: this.file.type || 'application/octet-stream',
                totalChunks: totalChunks
            });
            this.dc.send(metaMsg);
        } catch (e) {
            if (this.onError) this.onError('Failed to send metadata: ' + e.message);
            return;
        }

        var reader = new FileReader();
        var offset = 0;

        var sendNext = function () {
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

            var chunk = self.file.slice(offset, offset + CHUNK_SIZE);
            reader.onload = async function (e) {
                if (self._cancelled) return;

                var arrayBuffer = e.target.result;
                var isFinal = (offset + CHUNK_SIZE) >= self.file.size;

                try {
                    var data = arrayBuffer;

                    // Encrypt if encryption is configured
                    if (self.encryption && self.encryption.key) {
                        data = await self.encryption.encrypt(data, self.encryption.key, currentChunk);
                    }

                    // Create message: first 4 bytes = chunk index as Uint32, rest = chunk data
                    var header = new ArrayBuffer(8);
                    var headerView = new DataView(header);
                    headerView.setUint32(0, currentChunk, true); // chunk index
                    headerView.setUint8(4, isFinal ? 1 : 0); // final flag
                    headerView.setUint16(5, data.byteLength, true); // data length (max 64KB + overhead)

                    var combined = new Uint8Array(header.byteLength + data.byteLength);
                    combined.set(new Uint8Array(header), 0);
                    combined.set(new Uint8Array(data), header.byteLength);

                    self.dc.send(combined.buffer);

                    currentChunk++;
                    offset += CHUNK_SIZE;

                    if (self.onProgress) {
                        self.onProgress(Math.min(offset, self.file.size), self.file.size);
                    }

                    setTimeout(sendNext, 0);
                } catch (err) {
                    if (self.onError) self.onError('Send failed at chunk ' + currentChunk + ': ' + err.message);
                }
            };

            reader.onerror = function () {
                if (self.onError) self.onError('Failed to read file at chunk ' + currentChunk);
            };

            reader.readAsArrayBuffer(chunk);
        };

        sendNext();
    };

    FileSender.prototype.cancel = function () {
        this._cancelled = true;
    };

    window.P2P = window.P2P || {};
    window.P2P.FileSender = FileSender;
})();
