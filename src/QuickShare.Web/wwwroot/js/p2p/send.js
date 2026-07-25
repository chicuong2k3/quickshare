// QuickShare P2P send orchestration — vanilla JS
// Handles file selection, share link creation, WebRTC signaling, and chunked transfer.
(function () {
    'use strict';
    function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
    function formatBytes(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
    function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }
    function text(id, val) { var el = document.getElementById(id); if (el) el.textContent = val; }

    window.p2pSend = (function () {
        var state = {
            phase: 'idle', progress: 0, shareCode: '', shareLink: '', error: '',
            pin: '', _file: null, _intentionalStop: false,
            _signaling: null, _peerConnection: null, _dataChannel: null, _sender: null, _encKey: null
        };

        function setPhase(phase) { state.phase = phase; render(); }

        function render() {
            var sections = ['p2ps-idle', 'p2ps-creating', 'p2ps-waiting', 'p2ps-connecting', 'p2ps-transferring', 'p2ps-success', 'p2ps-failed'];
            sections.forEach(hide);

            var phaseMap = { idle: 'p2ps-idle', creating: 'p2ps-creating', waiting: 'p2ps-waiting', connecting: 'p2ps-connecting', transferring: 'p2ps-transferring', success: 'p2ps-success', failed: 'p2ps-failed' };
            var elId = phaseMap[state.phase];
            if (elId) show(elId);

            if (state.phase === 'waiting') {
                var linkInput = document.getElementById('p2ps-share-link');
                if (linkInput) linkInput.value = state.shareLink;
                var copyBtn = document.getElementById('p2ps-copy-text');
                var copyDone = document.getElementById('p2ps-copy-done');
                if (copyBtn) copyBtn.classList.remove('hidden');
                if (copyDone) copyDone.classList.add('hidden');
                renderQR(state.shareLink);
            }
            if (state.phase === 'transferring') {
                var nameEl = document.getElementById('p2ps-transfer-name');
                if (nameEl) nameEl.textContent = state._file ? state._file.name : '';
                updateProgress();
            }
            if (state.phase === 'success') {
                var fnEl = document.getElementById('p2ps-success-name');
                if (fnEl) fnEl.textContent = state._file ? state._file.name : '';
            }
            if (state.phase === 'failed') {
                text('p2ps-error-msg', state.error || 'Transfer failed. Please try again.');
            }
        }

        function updateProgress() {
            text('p2ps-progress-label', progressLabel());
            var bar = document.getElementById('p2ps-progress-bar');
            if (bar) { bar.style.width = state.progress + '%'; bar.setAttribute('aria-valuenow', state.progress); }
        }

        function progressLabel() {
            if (state.progress === 0) return 'Preparing\u2026';
            var l = state.progress + '%';
            if (state._file) l += ' \u00b7 ' + formatBytes(state._file.size);
            return l;
        }

        // --- File input & drag-and-drop ---

        function onFileSelect(event) {
            var file = event.target.files ? event.target.files[0] : null;
            if (!file) return;
            startWithFile(file);
            event.target.value = '';
        }

        function onDragOver(event) {
            event.preventDefault();
            event.stopPropagation();
            document.getElementById('idle-h1').classList.add('hidden');
            document.getElementById('drag-h1').classList.remove('hidden');
            document.getElementById('idle-sub').classList.add('hidden');
            document.getElementById('drag-sub').classList.remove('hidden');
            document.getElementById('dropzone-idle').classList.add('hidden');
            document.getElementById('dropzone-drag').classList.remove('hidden');
        }

        function onDragLeave(event) {
            event.preventDefault();
            event.stopPropagation();
            document.getElementById('idle-h1').classList.remove('hidden');
            document.getElementById('drag-h1').classList.add('hidden');
            document.getElementById('idle-sub').classList.remove('hidden');
            document.getElementById('drag-sub').classList.add('hidden');
            document.getElementById('dropzone-idle').classList.remove('hidden');
            document.getElementById('dropzone-drag').classList.add('hidden');
        }

        function onDrop(event) {
            event.preventDefault();
            event.stopPropagation();
            onDragLeave(event);
            var file = event.dataTransfer.files ? event.dataTransfer.files[0] : null;
            if (!file) return;
            startWithFile(file);
        }

        // --- Share creation ---

        function startWithFile(file) {
            state._file = file;
            setPhase('creating');

            var body = { FileName: file.name, FileSize: file.size, MimeType: file.type || 'application/octet-stream', BaseUrl: window.location.origin };
            if (state.pin) body.HasPin = true;

            var api = apiBase();
            fetch(api + '/api/shares/p2p', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            })
            .then(function (res) {
                if (!res.ok) return res.text().then(function (t) { throw new Error(t || 'Failed to create share'); });
                return res.json();
            })
            .then(function (data) {
                state.shareCode = data.shareCode;
                state.shareLink = data.shareLink || (window.location.origin + '/p2p/d/' + data.shareCode);
                state.phase = 'waiting';
                render();
                connectSignaling();
            })
            .catch(function (err) {
                state.error = err.message || 'Failed to create share link';
                setPhase('failed');
            });
        }

        // --- Signaling ---

        function connectSignaling() {
            if (!window.P2P || !window.P2P.SignalingClient) {
                state.error = 'Signaling client not loaded'; setPhase('failed'); return;
            }
            state._signaling = new window.P2P.SignalingClient(apiBase(), state.shareCode);
            state._signaling.onRegistered = function () {};
            state._signaling.onPeerJoined = function () { onPeerJoined(); };
            state._signaling.onPeerLeft = function () { cleanup(); };
            state._signaling.onError = function (msg) { state.error = msg || 'Connection lost'; setPhase('failed'); };
            state._signaling.onRelay = function (type, data) {
                if (type === 'answer') onAnswer(data);
                else if (type === 'ice' && data) state._peerConnection.addIceCandidate(data);
            };
            state._signaling.connect('sender', {
                name: state._file.name,
                size: state._file.size,
                mime: state._file.type || 'application/octet-stream'
            });
        }

        // --- WebRTC ---

        function onPeerJoined() {
            setPhase('connecting');
            if (!window.P2P.PeerConnection) { state.error = 'P2P modules not loaded'; setPhase('failed'); return; }

            state._peerConnection = new window.P2P.PeerConnection();
            state._peerConnection.onIceCandidate = function (c) {
                if (state._signaling) state._signaling.send('ice', c);
            };
            state._peerConnection.onConnectionState = function (s) {
                if ((s === 'failed' || s === 'disconnected') && (state.phase === 'transferring' || state.phase === 'connecting')) {
                    state.error = 'Connection ' + s;
                    setPhase('failed');
                }
            };

            // Derive encryption key from PIN if set
            var encPromise = Promise.resolve();
            if (state.pin) {
                var salt = state.pin + state.shareCode;
                encPromise = window.P2P.encryption.deriveKey(state.pin, salt).then(function (key) {
                    state._encKey = key;
                });
            }

            encPromise.then(function () {
                var channel = state._peerConnection.createDataChannel('filetransfer');
                if (!channel) { state.error = 'Failed to create data channel'; setPhase('failed'); return; }
                state._dataChannel = channel;
                channel.onopen = function () { onChannelOpen(channel); };
                channel.onerror = function () { state.error = 'Data channel error'; setPhase('failed'); };

                return state._peerConnection.createOffer();
            })
            .then(function (sdp) {
                if (state._signaling) state._signaling.send('offer', { sdp: sdp });
            })
            .catch(function (err) {
                state.error = 'WebRTC setup failed: ' + (err.message || err);
                setPhase('failed');
            });
        }

        function onAnswer(data) {
            var sdp = data.sdp || data;
            if (state._peerConnection) {
                state._peerConnection.setRemoteSdp(sdp).catch(function () {
                    state.error = 'Failed to set remote SDP'; setPhase('failed');
                });
            }
        }

        function onChannelOpen(channel) {
            if (!window.P2P.FileSender) { state.error = 'File sender module not loaded'; setPhase('failed'); return; }

            var enc = state._encKey ? { key: state._encKey, encrypt: window.P2P.encryption.encryptChunk } : null;
            state._sender = new window.P2P.FileSender(state._file, channel, enc);
            state._sender.onProgress = function (sent, total) {
                state.progress = Math.round((sent / total) * 100);
                updateProgress();
            };
            state._sender.onComplete = function () { state.progress = 100; setPhase('success'); };
            state._sender.onError = function (msg) { state.error = msg; setPhase('failed'); };

            setPhase('transferring');
            state._sender.start();
        }

        // --- Actions ---

        function togglePin() {
            var cb = document.getElementById('p2ps-enable-pin');
            var input = document.querySelector('.pin-input');
            if (cb && input) {
                if (cb.checked) input.classList.remove('hidden');
                else { input.classList.add('hidden'); input.value = ''; state.pin = ''; }
            }
        }

        function copyLink() {
            var input = document.getElementById('p2ps-share-link');
            if (!input) return;
            input.select();
            try { document.execCommand('copy'); } catch (e) { }
            navigator.clipboard.writeText(input.value).catch(function () {});
            var copyBtn = document.getElementById('p2ps-copy-text');
            var copyDone = document.getElementById('p2ps-copy-done');
            if (copyBtn) copyBtn.classList.add('hidden');
            if (copyDone) copyDone.classList.remove('hidden');
            setTimeout(function () {
                if (copyBtn) copyBtn.classList.remove('hidden');
                if (copyDone) copyDone.classList.add('hidden');
            }, 2000);
        }

        function cancel() { state._intentionalStop = true; cleanup(); setPhase('idle'); }
        function cancelTransfer() { state._intentionalStop = true; if (state._sender) state._sender.cancel(); cleanup(); setPhase('idle'); }

        function renderQR(text) {
            var el = document.getElementById('p2ps-qrcode');
            if (!el) return;
            el.innerHTML = '';
            if (!text || !window.qrcode) {
                if (!window.qrcode) el.textContent = 'QR library not loaded.';
                return;
            }
            try {
                var qr = window.qrcode(0, 'M');
                qr.addData(text);
                qr.make();
                var moduleCount = qr.getModuleCount();
                var cell = 6;
                var dim = moduleCount * cell;
                var parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="QR code for share link" style="image-rendering:pixelated"><rect width="' + dim + '" height="' + dim + '" fill="#fff"/>'];
                for (var r = 0; r < moduleCount; r++) {
                    for (var c = 0; c < moduleCount; c++) {
                        if (qr.isDark(r, c)) parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
                    }
                }
                parts.push('</svg>');
                el.innerHTML = parts.join('');
            } catch (e) {
                el.textContent = 'QR unavailable.';
            }
        }

        function reset() {
            cleanup();
            state._file = null;
            state.shareCode = '';
            state.shareLink = '';
            var qrEl = document.getElementById('p2ps-qrcode');
            if (qrEl) qrEl.innerHTML = '';
            state.progress = 0;
            state.error = '';
            state.pin = '';
            var pinInput = document.querySelector('.pin-input');
            if (pinInput) { pinInput.value = ''; pinInput.classList.add('hidden'); }
            var cb = document.getElementById('p2ps-enable-pin');
            if (cb) cb.checked = false;
            setPhase('idle');
        }

        function cleanup() {
            if (state._sender) { state._sender.cancel(); state._sender = null; }
            if (state._signaling) { state._signaling.close(); state._signaling = null; }
            if (state._peerConnection) { state._peerConnection.close(); state._peerConnection = null; }
            state._dataChannel = null;
            state._encKey = null;
        }

        // Expose public API
        return {
            onFileSelect: onFileSelect,
            onDragOver: onDragOver,
            onDragLeave: onDragLeave,
            onDrop: onDrop,
            togglePin: togglePin,
            copyLink: copyLink,
            cancel: cancel,
            cancelTransfer: cancelTransfer,
            reset: reset
        };
    })();
})();
