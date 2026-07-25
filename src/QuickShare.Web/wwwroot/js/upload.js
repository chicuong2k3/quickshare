// QuickShareCloud upload page logic.
// R2 Multipart Upload + Client-Side AES-256-GCM Encryption (Stories 1.5, 1.6)
(function () {
    'use strict';

    var MAX_FILE_BYTES = 5 * 1024 * 1024 * 1024; // 5 GB
    var PART_TARGET = 25 * 1024 * 1024; // 25 MB per part
    var CONCURRENT_PARTS = 4;

    function register(Alpine) {
        Alpine.data('uploadPage', function () {
            return {
                // State machine: idle | dragging | uploading | success | error
                state: 'idle',
                progress: 0,
                filename: '',
                link: '',
                expiresAt: '',
                error: '',
                expiry: '24h',
                copied: false,
                pasteTip: false,
                draggingActive: false,
                encryptionEnabled: false,
                _abortController: null,

                get isIdle() { return this.state === 'idle'; },
                get isDragging() { return this.state === 'dragging'; },
                get isUploading() { return this.state === 'uploading'; },
                get isSuccess() { return this.state === 'success'; },
                get isError() { return this.state === 'error'; },

                init: function () {
                    var self = this;
                    document.addEventListener('paste', function (e) { self.handlePaste(e); });
                    document.addEventListener('change', function (e) {
                        var input = e.target;
                        if (input && input.type === 'file' && input.files && input.files.length) {
                            self.onFileInput(e);
                        }
                    });
                },

                selectExpiry: function (value) {
                    this.expiry = value;
                },

                onDragOver: function (e) {
                    e.preventDefault();
                    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
                    this.draggingActive = true;
                    if (this.state === 'idle') this.state = 'dragging';
                },

                onDragLeave: function (e) {
                    e.preventDefault();
                    this.draggingActive = false;
                    if (this.state === 'dragging') this.state = 'idle';
                },

                onDrop: function (e) {
                    e.preventDefault();
                    this.draggingActive = false;
                    var files = e.dataTransfer && e.dataTransfer.files;
                    if (files && files.length) {
                        this.startUpload(files[0]);
                    } else {
                        this.state = 'idle';
                    }
                },

                onFileInput: function (e) {
                    var files = e.target && e.target.files;
                    if (files && files.length) this.startUpload(files[0]);
                },

                triggerFileInput: function () {
                    var input = document.querySelector('input[type="file"]');
                    if (input) input.click();
                },

                handlePaste: function (e) {
                    if (this.state === 'uploading') return;
                    var items = e.clipboardData && e.clipboardData.items;
                    if (!items) return;
                    var imageFile = null;
                    for (var i = 0; i < items.length; i++) {
                        var it = items[i];
                        if (it.kind === 'file' && it.type && it.type.indexOf('image/') === 0) {
                            imageFile = it.getAsFile();
                            if (imageFile) break;
                        }
                    }
                    if (imageFile) {
                        e.preventDefault();
                        this.startUpload(imageFile);
                    } else {
                        var self = this;
                        this.pasteTip = true;
                        setTimeout(function () { self.pasteTip = false; }, 2000);
                    }
                },

                // ===== MAIN UPLOAD ENTRY =====
                startUpload: function (file) {
                    if (!file) return;
                    if (file.size > MAX_FILE_BYTES) {
                        this.state = 'error';
                        this.error = 'File exceeds the 5 GB upload limit.';
                        return;
                    }
                    this.filename = file.name || 'pasted-image';
                    this.fileSize = file.size;
                    this.progress = 0;
                    this.error = '';
                    this.link = '';
                    this.copied = false;
                    this._encryptionUsed = false;
                    this.state = 'uploading';
                    this._startTime = Date.now();
                    this._uploadR2(file)['catch'](function (e) {
                        if (e.name === 'AbortError') return;
                        this.state = 'error';
                        this.error = e.message || 'Upload failed';
                    }.bind(this));
                },

                _apiBase: function () {
                    if (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl) {
                        return String(window.ZAPDROP_CONFIG.apiBaseUrl).replace(/\/+$/, '');
                    }
                    return '';
                },

                // ===== AES-256-GCM KEY GENERATION =====
                _generateEncryptionKey: function () {
                    return crypto.subtle.generateKey(
                        { name: 'AES-GCM', length: 256 },
                        true,
                        ['encrypt', 'decrypt']
                    ).then(function (key) {
                        return crypto.subtle.exportKey('raw', key).then(function (raw) {
                            var bytes = new Uint8Array(raw);
                            var binary = '';
                            for (var i = 0; i < bytes.length; i++) {
                                binary += String.fromCharCode(bytes[i]);
                            }
                            var base64url = btoa(binary)
                                .replace(/\+/g, '-')
                                .replace(/\//g, '_')
                                .replace(/=+$/, '');
                            return { key: key, base64url: base64url };
                        });
                    });
                },

                // ===== ENCRYPT FILE =====
                _encryptFileData: function (fileData, aesKey) {
                    var nonce = crypto.getRandomValues(new Uint8Array(12));
                    return crypto.subtle.encrypt(
                        { name: 'AES-GCM', iv: nonce },
                        aesKey,
                        fileData
                    ).then(function (encrypted) {
                        var combined = new Uint8Array(nonce.length + encrypted.byteLength);
                        combined.set(nonce, 0);
                        combined.set(new Uint8Array(encrypted), nonce.length);
                        return combined.buffer;
                    });
                },

                // ===== CALCULATE R2 PART NUMBERS =====
                _calculatePartCount: function (fileSize) {
                    if (fileSize <= PART_TARGET) return 1;
                    return Math.ceil(fileSize / PART_TARGET);
                },

                // ===== R2 MULTIPART UPLOAD =====
                _uploadR2: async function (file) {
                    var self = this;
                    var api = self._apiBase();

                    // Step 0: Optionally encrypt
                    var uploadBlob = file;
                    var encryptionKeyStr = null;
                    var originalSize = file.size;

                    if (self.encryptionEnabled) {
                        var keyInfo = await self._generateEncryptionKey();
                        var fileData = await file.arrayBuffer();
                        var encryptedBuf = await self._encryptFileData(fileData, keyInfo.key);
                        uploadBlob = new Blob([encryptedBuf]);
                        encryptionKeyStr = keyInfo.base64url;
                        self._encryptionUsed = true;
                    }

                    var effectiveSize = uploadBlob.size;
                    var totalParts = self._calculatePartCount(effectiveSize);
                    var partNumbers = [];
                    for (var i = 1; i <= totalParts; i++) partNumbers.push(i);

                    // Step 1: Start R2 multipart
                    self._abortController = new AbortController();
                    var signal = self._abortController.signal;

                    var startResp = await fetch(api + '/api/uploads/start-r2', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        signal: signal,
                        body: JSON.stringify({
                            fileName: file.name,
                            fileSize: originalSize,
                            contentType: file.type || 'application/octet-stream',
                            partNumbers: partNumbers
                        })
                    });

                    if (!startResp.ok) {
                        var errText = await startResp.text();
                        throw new Error(errText || 'Failed to start upload');
                    }

                    var session = await startResp.json();
                    var deliveryId = session.deliveryId;
                    var uploadId = session.uploadId;
                    var objectKey = session.objectKey;
                    var partList = session.parts;

                    // Store for cancel
                    self._deliveryId = deliveryId;
                    self._uploadId = uploadId;

                    // Step 2: Upload parts in concurrent batches
                    var completedParts = 0;
                    var partEtags = [];
                    var uploadErrors = [];

                    function uploadOnePart(partInfo, idx) {
                        var partNumber = partInfo.partNumber;
                        var url = partInfo.url;
                        var partSize = Math.ceil(effectiveSize / totalParts);
                        var start = idx * partSize;
                        var end = Math.min(start + partSize, effectiveSize);
                        var blob = uploadBlob.slice(start, end);

                        return fetch(url, {
                            method: 'PUT',
                            body: blob,
                            signal: signal
                        }).then(function (resp) {
                            if (!resp.ok) {
                                return resp.text().then(function (t) {
                                    throw new Error('Part ' + partNumber + ' failed: ' + t);
                                });
                            }
                            var etag = resp.headers.get('ETag') || '';
                            etag = etag.replace(/^"(.*)"$/, '$1');
                            partEtags[idx] = { partNumber: partNumber, etag: etag };
                            completedParts++;
                            self.progress = Math.round((completedParts / totalParts) * 100);
                            var elapsed = (Date.now() - self._startTime) / 1000;
                            if (elapsed > 0) {
                                var doneFrac = completedParts / totalParts;
                                var uploadedSoFar = doneFrac * effectiveSize;
                                var bps = uploadedSoFar / elapsed;
                                self.speed = self._formatBytes(bps) + '/s';
                                var remaining = (effectiveSize - uploadedSoFar) / bps;
                                self.eta = remaining < 60 ? Math.round(remaining) + 's' : Math.round(remaining / 60) + 'm';
                            }
                        });
                    }

                    for (var batchStart = 0; batchStart < totalParts; batchStart += CONCURRENT_PARTS) {
                        var batchEnd = Math.min(batchStart + CONCURRENT_PARTS, totalParts);
                        var batch = [];
                        for (var p = batchStart; p < batchEnd; p++) {
                            batch.push(uploadOnePart(partList[p], p));
                        }
                        var results = await Promise.allSettled(batch);
                        for (var r = 0; r < results.length; r++) {
                            if (results[r].status === 'rejected') {
                                uploadErrors.push(results[r].reason);
                            }
                        }
                        if (uploadErrors.length > 0) throw uploadErrors[0];
                    }

                    // Step 3: Commit R2 multipart
                    var commitResp = await fetch(api + '/api/uploads/commit-r2', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        signal: signal,
                        body: JSON.stringify({
                            deliveryId: deliveryId,
                            uploadId: uploadId,
                            objectKey: objectKey,
                            fileName: file.name,
                            contentType: file.type || 'application/octet-stream',
                            fileSize: originalSize,
                            parts: partEtags
                        })
                    });

                    if (!commitResp.ok) {
                        var commitErr = await commitResp.text();
                        throw new Error(commitErr || 'Failed to finalize upload');
                    }

                    var result = await commitResp.json();
                    var shareLink = result.shareLink;

                    if (encryptionKeyStr) {
                        shareLink += '#key=' + encryptionKeyStr;
                    }

                    self.link = shareLink;
                    self.state = 'success';
                    self._renderQR(shareLink);
                    self._copyLink();
                },

                // ===== CANCEL =====
                cancelUpload: function () {
                    if (this._abortController) {
                        this._abortController.abort();
                    }
                    var self = this;
                    if (self._deliveryId && self._uploadId) {
                        var api = self._apiBase();
                        fetch(api + '/api/uploads/abort-r2', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                deliveryId: self._deliveryId,
                                uploadId: self._uploadId
                            })
                        })['catch'](function () {});
                    }
                    self.reset();
                    this.state = 'idle';
                },

                // ===== RESET =====
                reset: function () {
                    this.state = 'idle';
                    this.progress = 0;
                    this.filename = '';
                    this.link = '';
                    this.expiresAt = '';
                    this.error = '';
                    this.copied = false;
                    this.encryptionEnabled = false;
                    this._encryptionUsed = false;
                    this._abortController = null;
                    this._deliveryId = null;
                    this._uploadId = null;
                    this.uploadedBytes = 0;
                    this.fileSize = 0;
                    this.speed = '';
                    this.eta = '';
                },

                // ===== COPY LINK =====
                copyLink: function () {
                    return this._copyLink();
                },

                _copyLink: function () {
                    var self = this;
                    if (!this.link) return Promise.resolve();
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        return navigator.clipboard.writeText(this.link).then(
                            function () { self.copied = true; },
                            function () { self._fallbackCopy(); }
                        );
                    }
                    this._fallbackCopy();
                    return Promise.resolve();
                },

                _fallbackCopy: function () {
                    var input = document.getElementById('share-link');
                    if (!input) return;
                    try {
                        input.removeAttribute('readonly');
                        input.focus();
                        input.select();
                        document.execCommand('copy');
                        this.copied = true;
                    } catch (_) {
                        this.copied = false;
                    } finally {
                        input.setAttribute('readonly', '');
                    }
                },

                // ===== QR CODE =====
                _renderQR: function (text) {
                    var el = document.getElementById('qrcode');
                    if (!el) return;
                    el.innerHTML = '';
                    if (!text) return;
                    if (!window.qrcode) {
                        el.textContent = 'QR library not loaded.';
                        return;
                    }
                    try {
                        var qr = window.qrcode(0, 'M');
                        qr.addData(text);
                        qr.make();
                        var moduleCount = qr.getModuleCount();
                        var cell = 6;
                        var dim = moduleCount * cell;
                        var parts = [];
                        parts.push('<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="QR code for share link" style="image-rendering:pixelated">');
                        parts.push('<rect width="' + dim + '" height="' + dim + '" fill="#ffffff"/>');
                        for (var r = 0; r < moduleCount; r++) {
                            for (var c = 0; c < moduleCount; c++) {
                                if (qr.isDark(r, c)) {
                                    parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
                                }
                            }
                        }
                        parts.push('</svg>');
                        el.innerHTML = parts.join('');
                    } catch (e) {
                        el.textContent = 'QR unavailable.';
                    }
                },

                // ===== COMPUTED =====
                get expiryLabel() {
                    if (!this.expiresAt) return '';
                    try {
                        var d = new Date(this.expiresAt);
                        if (isNaN(d.getTime())) return '';
                        return 'Expires: ' + d.toLocaleString(undefined, {
                            day: 'numeric', month: 'short', year: 'numeric',
                            hour: '2-digit', minute: '2-digit'
                        });
                    } catch (_) {
                        return '';
                    }
                },

                get progressWidth() {
                    return 'width: ' + this.progress + '%;';
                },

                _formatBytes: function (bytes) {
                    if (bytes < 1024) return bytes + ' B';
                    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
                    if (bytes < 1073741824) return (bytes / 1048576).toFixed(1) + ' MB';
                    return (bytes / 1073741824).toFixed(1) + ' GB';
                },

                get progressLabel() {
                    if (this.progress === 0) return 'Starting...';
                    var label = this.progress + '%';
                    if (this.uploadedBytes && this.fileSize)
                        label += ' \u00b7 ' + this._formatBytes(this.uploadedBytes) + ' / ' + this._formatBytes(this.fileSize);
                    if (this.speed) label += ' \u00b7 ' + this.speed;
                    if (this.eta) label += ' \u00b7 ~' + this.eta;
                    return label;
                }
            };
        });
    }

    if (window.Alpine) {
        register(window.Alpine);
    } else {
        window.addEventListener('alpine:init', function () {
            if (window.Alpine) register(window.Alpine);
        });
    }
})();
