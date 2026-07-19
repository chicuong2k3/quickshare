// QuickShareCloud upload page logic.
// Framework-light: Alpine.js (registered via alpine:init, injected by RizzyUI's RzHeadOutlet)
// drives the UI states; this file owns drag/drop, paste, XHR upload with progress,
// clipboard auto-copy, and client-side QR rendering. No external runtime calls.
(function () {
    'use strict';

    const MAX_FILE_BYTES = 5 * 1024 * 1024 * 1024; // 5 GB (Free plan limit)
    const CHUNK_SIZE = 25 * 1024 * 1024; // 25MB

    function register(Alpine) {
        Alpine.data('uploadPage', function () {
            return {
                // UI state machine: idle | dragging | uploading | success | error
                state: 'idle',
                progress: 0,
                filename: '',
                link: '',
                expiresAt: '',
                error: '',
                expiry: '24h', // '24h' (default) | '7d'
                copied: false,
                pasteTip: false,
                draggingActive: false,
                _xhr: null,

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
                    if (files && files.length) {
                        this.startUpload(files[0]);
                    }
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
                        // Non-image clipboard content → tooltip.
                        var self = this;
                        this.pasteTip = true;
                        setTimeout(function () { self.pasteTip = false; }, 2000);
                    }
                },

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
                    this.uploadedBytes = 0;
                    this.speed = '';
                    this.eta = '';
                    this.error = '';
                    this.link = '';
                    this.copied = false;
                    this.state = 'uploading';
                    this._startTime = Date.now();
                    this._uploadChunked(file);
                },

                _apiBase: function () {
                    if (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl) {
                        return String(window.ZAPDROP_CONFIG.apiBaseUrl).replace(/\/+$/, '');
                    }
                    return '';
                },

                _uploadChunked: async function (file) {
                    var self = this;
                    var api = self._apiBase();
                    try {
                        var startResp = await fetch(api + '/api/uploads/start', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ fileName: file.name, fileSize: file.size, mimeType: file.type || 'application/octet-stream' })
                        });
                        if (!startResp.ok) {
                            var err = await startResp.text();
                            self.state = 'error';
                            self.error = err || 'Failed to start upload';
                            return;
                        }
                        var session = await startResp.json();
                        var uploadId = session.uploadId;
                        var totalChunks = Math.ceil(file.size / CHUNK_SIZE);

                        for (var i = 0; i < totalChunks; i++) {
                            var start = i * CHUNK_SIZE;
                            var end = Math.min(start + CHUNK_SIZE, file.size);
                            var chunk = file.slice(start, end);

                            var chunkResp = await fetch(api + '/api/uploads/' + uploadId + '/chunks/' + i, {
                                method: 'POST',
                                body: chunk
                            });
                            if (!chunkResp.ok) {
                                var chunkErr = await chunkResp.text();
                                self.state = 'error';
                                self.error = 'Upload failed at chunk ' + i + ': ' + chunkErr + '. Check your connection.';
                                return;
                            }
                            self.uploadedBytes = end;
                            self.progress = Math.round((end / file.size) * 100);
                            var elapsed = (Date.now() - self._startTime) / 1000;
                            if (elapsed > 0) {
                                var bps = end / elapsed;
                                self.speed = self._formatBytes(bps) + '/s';
                                var remaining = (file.size - end) / bps;
                                self.eta = remaining < 60 ? Math.round(remaining) + 's' : Math.round(remaining / 60) + 'm';
                            }
                        }

                        var commitResp = await fetch(api + '/api/uploads/' + uploadId + '/commit', { method: 'POST' });
                        var commitBody = await commitResp.json();
                        if (!commitResp.ok) {
                            self.state = 'error';
                            self.error = 'Commit failed: ' + (commitBody.errors || '');
                            return;
                        }
                        self.link = commitBody.shareLink;
                        self.shareId = commitBody.shareId;
                        self.state = 'success';
                        self._renderQR(self.link);
                        self._copyLink();
                    } catch (e) {
                        self.state = 'error';
                        self.error = 'Network error — could not reach the upload service.';
                    }
                },

                cancelUpload: function () {
                    this.state = 'idle';
                    this.progress = 0;
                },

                reset: function () {
                    this.state = 'idle';
                    this.progress = 0;
                    this.filename = '';
                    this.link = '';
                    this.expiresAt = '';
                    this.error = '';
                    this.copied = false;
                },

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
                        var qr = window.qrcode(0, 'M'); // typeNumber 0 = auto-size, ECC level M
                        qr.addData(text);
                        qr.make();
                        var moduleCount = qr.getModuleCount();
                        var cell = 6; // px per module → ≥ 200px for typical URLs
                        var dim = moduleCount * cell;
                        var parts = [];
                        parts.push('<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="QR code for the share link" style="image-rendering:pixelated">');
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
                        label += ' · ' + this._formatBytes(this.uploadedBytes) + ' / ' + this._formatBytes(this.fileSize);
                    if (this.speed) label += ' · ' + this.speed;
                    if (this.eta) label += ' · ~' + this.eta;
                    return label;
                },

                get isIdle() { return this.state === 'idle'; },
                get isDragging() { return this.state === 'dragging'; },
                get isUploading() { return this.state === 'uploading'; },
                get isSuccess() { return this.state === 'success'; },
                get isError() { return this.state === 'error'; }
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
