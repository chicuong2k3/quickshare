// Chunked file receiver over WebRTC data channel.
// Uses StreamSaver for files >500 MB to avoid OOM.
(function () {
    'use strict';

    var STREAMSAVER_CDN = 'https://cdn.jsdelivr.net/npm/streamsaver@2.0.6/StreamSaver.min.js';
    var BLOB_THRESHOLD = 500 * 1024 * 1024; // 500MB
    var HEADER_SIZE = 8; // 4 bytes index + 1 byte final + 2 bytes length + 1 reserved

    function FileReceiver(dataChannel, encryption) {
        this.dc = dataChannel;
        this.encryption = encryption || null;
        this._fileInfo = null;
        this._receivedBytes = 0;
        this._parts = [];
        this._writeStream = null;
        this._writer = null;
        this._expectedChunk = 0;
        this._cancelled = false;

        // Event callbacks
        this.onProgress = null;
        this.onComplete = null;
        this.onError = null;
        this.onFileInfo = null;
    }

    FileReceiver.prototype.start = function () {
        var self = this;

        this.dc.binaryType = 'arraybuffer';

        this.dc.onmessage = function (event) {
            if (self._cancelled) return;

            try {
                self._handleMessage(event.data);
            } catch (e) {
                if (self.onError) self.onError('Receive error: ' + e.message);
            }
        };

        this.dc.onclose = function () {
            if (!self._cancelled && self._fileInfo && self._receivedBytes < self._fileInfo.size) {
                if (self.onError) self.onError('Transfer interrupted');
            }
        };
    };

    FileReceiver.prototype._handleMessage = function (data) {
        var self = this;

        if (typeof data === 'string') {
            // Meta message (JSON string)
            try {
                var meta = JSON.parse(data);
                if (meta.type === 'meta') {
                    this._fileInfo = {
                        name: meta.name,
                        size: meta.size,
                        mime: meta.mime,
                        totalChunks: meta.totalChunks
                    };
                    if (this.onFileInfo) this.onFileInfo(this._fileInfo);

                    // Initialize StreamSaver for large files
                    if (meta.size > BLOB_THRESHOLD) {
                        this._initStreamSaver(meta);
                    }
                }
            } catch (e) {
                // Ignore non-JSON strings
            }
            return;
        }

        // Binary chunk
        if (!this._fileInfo) {
            if (this.onError) this.onError('Received data before file metadata');
            return;
        }

        var header = new Uint8Array(data.slice(0, HEADER_SIZE));
        var view = new DataView(header.buffer);
        var chunkIndex = view.getUint32(0, true);
        var isFinal = view.getUint8(4) === 1;
        var dataLength = view.getUint16(5, true);

        var chunkData = data.slice(HEADER_SIZE, HEADER_SIZE + (dataLength || data.byteLength - HEADER_SIZE));

        this._processChunk(chunkIndex, isFinal, chunkData);
    };

    FileReceiver.prototype._processChunk = async function (index, isFinal, chunkData) {
        var self = this;

        // Decrypt if encryption is configured
        if (this.encryption && this.encryption.key) {
            try {
                chunkData = await this.encryption.decrypt(chunkData, this.encryption.key, index);
            } catch (e) {
                if (this.onError) this.onError('Decryption failed at chunk ' + index);
                return;
            }
        }

        this._receivedBytes += chunkData.byteLength;

        if (this._writer) {
            // StreamSaver mode
            try {
                await this._writer.write(new Uint8Array(chunkData));
            } catch (e) {
                if (this.onError) this.onError('Stream write error: ' + e.message);
                return;
            }

            if (isFinal || this._receivedBytes >= this._fileInfo.size) {
                try {
                    await this._writer.close();
                } catch (e) {
                    // Stream may already close
                }
                if (this.onProgress) this.onProgress(this._fileInfo.size, this._fileInfo.size);
                if (this.onComplete) this.onComplete();
            }
        } else {
            // Blob mode (files <= 500MB)
            this._parts.push(chunkData);

            if (this.onProgress) {
                this.onProgress(this._receivedBytes, this._fileInfo.size);
            }

            if (isFinal || this._receivedBytes >= this._fileInfo.size) {
                this._finalizeBlobDownload();
            }
        }

        if (this.onProgress) {
            this.onProgress(
                Math.min(this._receivedBytes, this._fileInfo.size),
                this._fileInfo.size
            );
        }
    };

    FileReceiver.prototype._finalizeBlobDownload = function () {
        var blob = new Blob(this._parts, { type: this._fileInfo.mime });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = this._fileInfo.name;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () {
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }, 1000);

        this._parts = [];
        if (this.onComplete) this.onComplete();
    };

    FileReceiver.prototype._initStreamSaver = function (meta) {
        var self = this;

        function loadStreamSaver() {
            if (window.streamSaver) {
                setupWriteStream(window.streamSaver);
                return;
            }

            var script = document.createElement('script');
            script.src = STREAMSAVER_CDN;
            script.onload = function () {
                if (window.streamSaver) {
                    setupWriteStream(window.streamSaver);
                } else {
                    // StreamSaver not available, fall back to blob
                    if (self.onError) self.onError('StreamSaver not available, download may fail for large files');
                }
            };
            script.onerror = function () {
                if (self.onError) self.onError('Failed to load StreamSaver, falling back to in-memory download');
            };
            document.head.appendChild(script);
        }

        function setupWriteStream(streamSaver) {
            try {
                var fileStream = streamSaver.createWriteStream(meta.name, {
                    size: meta.size,
                    mimeType: meta.mime
                });
                self._writer = fileStream.getWriter();
            } catch (e) {
                if (self.onError) self.onError('StreamSaver setup failed: ' + e.message);
            }
        }

        loadStreamSaver();
    };

    FileReceiver.prototype.cancel = function () {
        this._cancelled = true;
        if (this._writer) {
            try {
                this._writer.abort();
            } catch (e) { }
        }
        this._parts = [];
    };

    window.P2P = window.P2P || {};
    window.P2P.FileReceiver = FileReceiver;
})();
