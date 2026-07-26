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
            _signaling: null, _peerConnections: {}, _channels: {}, _senders: {}, _senderProgress: {},
            _receiverCount: 0, _encKey: null
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
            state._signaling.onPeerJoined = function (receiverId) { onPeerJoined(receiverId); };
            state._signaling.onPeerLeft = function (message, receiverId) {
                if (receiverId) onReceiverLeft(receiverId);
            };
            state._signaling.onError = function (msg) { state.error = msg || 'Connection lost'; setPhase('failed'); };
            state._signaling.onRelay = function (type, data, receiverId) {
                if (type === 'answer') onAnswer(data, receiverId);
                else if (type === 'ice' && data && receiverId) {
                    var pc = state._peerConnections[receiverId];
                    if (pc) pc.addIceCandidate(data);
                }
            };
            state._signaling.connect('sender', {
                name: state._file.name,
                size: state._file.size,
                mime: state._file.type || 'application/octet-stream'
            });
        }

        // --- WebRTC ---

        function onPeerJoined(receiverId) {
            if (state.phase === 'idle' || state.phase === 'failed') return;
            if (state.phase !== 'connecting' && state.phase !== 'transferring') {
                setPhase('connecting');
            }

            if (!window.P2P.PeerConnection) { state.error = 'P2P modules not loaded'; setPhase('failed'); return; }

            state._receiverCount++;
            updateReceiverCount();

            var pc = new window.P2P.PeerConnection();
            state._peerConnections[receiverId] = pc;

            pc.onIceCandidate = function (c) {
                if (state._signaling) {
                    state._signaling.sendTo('ice', { candidate: c }, receiverId);
                }
            };
            pc.onConnectionState = function (s) {
                if ((s === 'failed' || s === 'disconnected') && (state.phase === 'transferring' || state.phase === 'connecting')) {
                    onReceiverLeft(receiverId);
                }
            };

            var encPromise = Promise.resolve();
            if (state.pin) {
                var salt = state.pin + state.shareCode;
                encPromise = window.P2P.encryption.deriveKey(state.pin, salt).then(function (key) {
                    state._encKey = key;
                });
            }

            encPromise.then(function () {
                var channel = pc.createDataChannel('filetransfer');
                if (!channel) { onReceiverLeft(receiverId); return; }
                state._channels[receiverId] = channel;
                channel.onopen = function () { onChannelOpen(receiverId, channel); };
                channel.onerror = function () { onReceiverLeft(receiverId); };

                return pc.createOffer();
            })
            .then(function (sdp) {
                if (state._signaling) {
                    state._signaling.sendTo('offer', { sdp: sdp }, receiverId);
                }
            })
            .catch(function () {
                onReceiverLeft(receiverId);
            });
        }

        function onAnswer(data, receiverId) {
            var sdp = data.sdp || data;
            var pc = state._peerConnections[receiverId];
            if (pc) {
                pc.setRemoteSdp(sdp).catch(function () {});
            }
        }

        function onChannelOpen(receiverId, channel) {
            if (!window.P2P.FileSender) { state.error = 'File sender module not loaded'; setPhase('failed'); return; }

            var enc = state._encKey ? { key: state._encKey, encrypt: window.P2P.encryption.encryptChunk } : null;
            var sender = new window.P2P.FileSender(state._file, channel, enc);
            state._senders[receiverId] = sender;
            state._senderProgress[receiverId] = 0;

            sender.onProgress = function (sent, total) {
                state._senderProgress[receiverId] = sent / total;
                var totalPct = 0;
                var count = 0;
                var keys = Object.keys(state._senderProgress);
                for (var i = 0; i < keys.length; i++) {
                    totalPct += state._senderProgress[keys[i]];
                    count++;
                }
                state.progress = count > 0 ? Math.round((totalPct / count) * 100) : 0;
                updateProgress();
            };
            sender.onComplete = function () {
                delete state._senders[receiverId];
                delete state._channels[receiverId];
                delete state._senderProgress[receiverId];
                state._receiverCount--;
                if (state._receiverCount <= 0) {
                    state.progress = 100;
                    setPhase('success');
                }
                updateReceiverCount();
            };
            sender.onError = function () {
                onReceiverLeft(receiverId);
            };

            if (state.phase !== 'transferring') {
                setPhase('transferring');
            }
            sender.start();
        }

        function onReceiverLeft(receiverId) {
            if (state._senders[receiverId]) {
                state._senders[receiverId].cancel();
                delete state._senders[receiverId];
            }
            if (state._channels[receiverId]) {
                delete state._channels[receiverId];
            }
            if (state._peerConnections[receiverId]) {
                state._peerConnections[receiverId].close();
                delete state._peerConnections[receiverId];
            }
            delete state._senderProgress[receiverId];
            state._receiverCount = Math.max(0, state._receiverCount - 1);
            updateReceiverCount();

            if (state._receiverCount <= 0 && (state.phase === 'transferring' || state.phase === 'connecting')) {
                setPhase('waiting');
            }
        }

        function updateReceiverCount() {
            var el = document.getElementById('p2ps-receiver-count');
            var el2 = document.getElementById('p2ps-receiver-count-transfer');
            if (state._receiverCount > 0) {
                var text = state._receiverCount + ' receiver' + (state._receiverCount > 1 ? 's' : '') + ' connected';
                if (el) { el.textContent = text; el.style.display = ''; }
                if (el2) { el2.textContent = 'Sending to ' + state._receiverCount + ' receiver' + (state._receiverCount > 1 ? 's' : ''); el2.style.display = ''; }
            } else {
                if (el) el.style.display = 'none';
                if (el2) el2.style.display = 'none';
            }
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
        function cancelTransfer() { state._intentionalStop = true; cleanup(); setPhase('idle'); }

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
            var recvIds = Object.keys(state._senders);
            for (var i = 0; i < recvIds.length; i++) {
                if (state._senders[recvIds[i]]) state._senders[recvIds[i]].cancel();
            }
            state._senders = {};
            state._senderProgress = {};

            var pcIds = Object.keys(state._peerConnections);
            for (var j = 0; j < pcIds.length; j++) {
                if (state._peerConnections[pcIds[j]]) state._peerConnections[pcIds[j]].close();
            }
            state._peerConnections = {};
            state._channels = {};

            if (state._signaling) { state._signaling.close(); state._signaling = null; }
            state._encKey = null;
            state._receiverCount = 0;
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
