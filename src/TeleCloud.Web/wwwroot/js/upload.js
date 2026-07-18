// QuickShareCloud upload page logic.
// Framework-light: Alpine.js (registered via alpine:init, injected by RizzyUI's RzHeadOutlet)
// drives the UI states; this file owns drag/drop, paste, XHR upload with progress,
// clipboard auto-copy, and client-side QR rendering. No external runtime calls.
(function () {
    'use strict';

    var MAX_UPLOAD_BYTES = 2 * 1024 * 1024 * 1024; // 2 GB — matches server policy.

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
                    if (file.size > MAX_UPLOAD_BYTES) {
                        this.state = 'error';
                        this.error = 'File exceeds the 2 GB upload limit.';
                        return;
                    }
                    this.filename = file.name || 'pasted-image';
                    this.progress = 0;
                    this.error = '';
                    this.link = '';
                    this.copied = false;
                    this.state = 'uploading';
                    this._upload(file);
                },

                _apiBase: function () {
                    if (window.QUICKSHARE_CONFIG && window.QUICKSHARE_CONFIG.apiBaseUrl) {
                        return String(window.QUICKSHARE_CONFIG.apiBaseUrl).replace(/\/+$/, '');
                    }
                    return '';
                },

                _upload: function (file) {
                    var self = this;
                    var url = this._apiBase() + '/api/temp';
                    var fd = new FormData();
                    fd.append('file', file);
                    fd.append('expiry', this.expiry);

                    var xhr = new XMLHttpRequest();
                    this._xhr = xhr;
                    xhr.open('POST', url);
                    try { xhr.responseType = 'json'; } catch (_) { /* IE; responseType unsupported */ }

                    xhr.upload.onprogress = function (e) {
                        if (e.lengthComputable) {
                            self.progress = Math.max(0, Math.min(100, Math.round((e.loaded / e.total) * 100)));
                        }
                    };

                    xhr.onload = function () {
                        var body = xhr.response;
                        if (typeof body === 'string' && body) {
                            try { body = JSON.parse(body); } catch (_) { body = null; }
                        }
                        if (xhr.status === 201 && body) {
                            self.link = body.link || '';
                            self.expiresAt = body.expiresAt || '';
                            self.state = 'success';
                            self.copied = false;
                            self._renderQR(self.link);
                            self._copyLink();
                        } else if (xhr.status === 413) {
                            self.state = 'error';
                            self.error = 'File exceeds the 2 GB upload limit.';
                        } else if (xhr.status === 400) {
                            self.state = 'error';
                            self.error = self._extractError(xhr, body) || 'The request was rejected (invalid file or expiry).';
                        } else if (xhr.status >= 400) {
                            self.state = 'error';
                            self.error = self._extractError(xhr, body) || ('Upload failed (HTTP ' + xhr.status + ').');
                        } else {
                            self.state = 'error';
                            self.error = 'Unexpected response from server.';
                        }
                    };

                    xhr.onerror = function () {
                        self.state = 'error';
                        self.error = 'Network error — could not reach the upload service (check CORS/connectivity).';
                    };

                    xhr.onabort = function () {
                        self.state = 'idle';
                        self.progress = 0;
                    };

                    xhr.send(fd);
                },

                _extractError: function (xhr, body) {
                    if (body && typeof body === 'object') {
                        if (body.errors && typeof body.errors === 'object') {
                            try {
                                var flat = [];
                                for (var k in body.errors) {
                                    if (Object.prototype.hasOwnProperty.call(body.errors, k)) {
                                        var v = body.errors[k];
                                        if (Array.isArray(v)) flat = flat.concat(v);
                                        else if (typeof v === 'string') flat.push(v);
                                    }
                                }
                                if (flat.length) return flat.join(' ');
                            } catch (_) {}
                        }
                        if (typeof body.message === 'string' && body.message) return body.message;
                        if (typeof body.error === 'string' && body.error) return body.error;
                        if (typeof body.detail === 'string' && body.detail) return body.detail;
                    }
                    return '';
                },

                cancelUpload: function () {
                    if (this._xhr) {
                        try { this._xhr.abort(); } catch (_) {}
                    }
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
