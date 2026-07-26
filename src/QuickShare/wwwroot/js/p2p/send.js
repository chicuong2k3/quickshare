// QuickShare P2P send orchestration — vanilla JS
// Handles file selection, share link creation, WebRTC signaling, and multi-receiver transfer.
// The sender page stays on the 'waiting' phase (link + QR always visible) while
// receivers connect, receive, and complete independently. Each receiver's status
// is shown in a dynamic list below the QR code.
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
            _signaling: null, _encKey: null,
            _receivers: {}, _receiverIndex: 0
        };

        function setPhase(phase) { state.phase = phase; render(); }

        function render() {
            var sections = ['p2ps-idle', 'p2ps-pin-dialog', 'p2ps-creating', 'p2ps-waiting', 'p2ps-failed'];
            sections.forEach(hide);

            var phaseMap = { idle: 'p2ps-idle', pin: 'p2ps-pin-dialog', creating: 'p2ps-creating', waiting: 'p2ps-waiting', failed: 'p2ps-failed' };
            var elId = phaseMap[state.phase];
            if (elId) show(elId);

            if (state.phase === 'pin') {
                // focus first OTP input
                setTimeout(function () {
                    var first = document.querySelector('#p2ps-otp .db-otp__input');
                    if (first) first.focus();
                }, 100);
            }

            if (state.phase === 'waiting') {
                var linkInput = document.getElementById('p2ps-share-link');
                if (linkInput) linkInput.value = state.shareLink;
                var copyBtn = document.getElementById('p2ps-copy-text');
                var copyDone = document.getElementById('p2ps-copy-done');
                if (copyBtn) copyBtn.classList.remove('hidden');
                if (copyDone) copyDone.classList.add('hidden');
                renderQR(state.shareLink);
            }
            if (state.phase === 'failed') {
                text('p2ps-error-msg', state.error || 'Transfer failed. Please try again.');
            }
        }

        function statusLabel(s) {
            if (s === 'connecting') return 'Connecting\u2026';
            if (s === 'receiving') return 'Receiving';
            if (s === 'done') return 'Done';
            return 'Disconnected';
        }

        function renderReceivers() {
            var container = document.getElementById('p2ps-receiver-list');
            var itemsEl = document.getElementById('p2ps-receiver-items');
            if (!container || !itemsEl) return;

            var ids = Object.keys(state._receivers);
            if (ids.length === 0) { container.style.display = 'none'; return; }

            container.style.display = '';
            var html = '';
            for (var i = 0; i < ids.length; i++) {
                var r = state._receivers[ids[i]];
                var pct = Math.round(r.progress * 100);
                var statusClass = 'rstatus-' + r.status;
                html += '<div class="p2p-receiver-entry" style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;border:1px solid var(--db-color-border);border-radius:var(--db-radius-2,8px);margin-bottom:0.5rem;background:var(--db-color-surface,var(--db-color-bg))">';
                html += '<span style="font-weight:600;font-size:0.75rem;color:var(--db-color-text-muted);min-width:1.75rem">#' + r.index + '</span>';
                html += '<span style="font-size:0.75rem;font-family:monospace;color:var(--db-color-text-muted);min-width:4rem">' + ids[i].slice(0, 6) + '\u2026</span>';
                html += '<span class="' + statusClass + '" style="font-size:0.75rem;font-weight:500;padding:0.125rem 0.5rem;border-radius:999px;min-width:5.5rem;text-align:center">' + statusLabel(r.status) + '</span>';
                if (r.status === 'receiving') {
                    html += '<div style="flex:1;min-width:80px"><div style="height:4px;border-radius:2px;background:var(--db-color-border);overflow:hidden"><div style="height:100%;width:' + pct + '%;border-radius:2px;background:var(--db-color-accent);transition:width 0.3s ease"></div></div></div>';
                    html += '<span style="font-size:0.75rem;color:var(--db-color-text-muted);min-width:3rem;text-align:right">' + pct + '%</span>';
                }
                if (r.status === 'done') {
                    html += '<span style="font-size:0.75rem;color:var(--db-color-text-muted);margin-left:auto">' + formatBytes(state._file ? state._file.size : 0) + '</span>';
                }
                html += '</div>';
            }
            itemsEl.innerHTML = html;
        }

        // --- File input & drag-and-drop ---

        function onFileSelect(event) {
            var file = event.target.files ? event.target.files[0] : null;
            if (!file) return;
            state._file = file;
            setPhase('pin');
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
            var dz = document.querySelector('.p2p-dropzone');
            if (dz) dz.classList.add('active');
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
            var dz = document.querySelector('.p2p-dropzone');
            if (dz) dz.classList.remove('active');
        }

        function onDrop(event) {
            event.preventDefault();
            event.stopPropagation();
            onDragLeave(event);
            var file = event.dataTransfer.files ? event.dataTransfer.files[0] : null;
            if (!file) return;
            state._file = file;
            setPhase('pin');
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
                    var rcv = state._receivers[receiverId];
                    if (rcv && rcv.pc) rcv.pc.addIceCandidate(data);
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
            if (!window.P2P.PeerConnection) { state.error = 'P2P modules not loaded'; setPhase('failed'); return; }

            state._receiverIndex++;
            state._receivers[receiverId] = {
                index: state._receiverIndex,
                status: 'connecting',
                progress: 0,
                pc: null,
                channel: null,
                sender: null,
                fileName: state._file ? state._file.name : ''
            };
            renderReceivers();

            var pc = new window.P2P.PeerConnection();
            state._receivers[receiverId].pc = pc;

            pc.onIceCandidate = function (c) {
                if (state._signaling) {
                    state._signaling.sendTo('ice', { candidate: c }, receiverId);
                }
            };
            pc.onConnectionState = function (s) {
                if (s === 'failed' || s === 'disconnected') {
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
                var rcv = state._receivers[receiverId];
                if (!rcv) return;
                var channel = pc.createDataChannel('filetransfer');
                if (!channel) { onReceiverLeft(receiverId); return; }
                rcv.channel = channel;
                channel.onopen = function () { onChannelOpen(receiverId); };
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
            var rcv = state._receivers[receiverId];
            if (rcv && rcv.pc) {
                rcv.pc.setRemoteSdp(sdp).catch(function () {});
            }
        }

        function onChannelOpen(receiverId) {
            if (!window.P2P.FileSender) { state.error = 'File sender module not loaded'; setPhase('failed'); return; }

            var rcv = state._receivers[receiverId];
            if (!rcv) return;
            rcv.status = 'receiving';
            renderReceivers();

            var channel = rcv.channel;
            var enc = state._encKey ? { key: state._encKey, encrypt: window.P2P.encryption.encryptChunk } : null;
            var sender = new window.P2P.FileSender(state._file, channel, enc);
            rcv.sender = sender;

            sender.onProgress = function (sent, total) {
                if (!state._receivers[receiverId]) return;
                state._receivers[receiverId].progress = sent / total;
                renderReceivers();
            };
            sender.onComplete = function () {
                var r = state._receivers[receiverId];
                if (!r) return;
                r.status = 'done';
                r.sender = null;
                r.channel = null;
                renderReceivers();
            };
            sender.onError = function () {
                onReceiverLeft(receiverId);
            };

            sender.start();
        }

        function onReceiverLeft(receiverId) {
            if (!receiverId) return;
            var rcv = state._receivers[receiverId];
            if (!rcv || rcv.status === 'done' || rcv.status === 'disconnected') return;

            if (rcv.sender) { rcv.sender.cancel(); rcv.sender = null; }
            if (rcv.channel) { rcv.channel = null; }
            if (rcv.pc) { rcv.pc.close(); rcv.pc = null; }

            rcv.status = 'disconnected';
            renderReceivers();
        }

        // --- PIN dialog ---

        function readOtp() {
            var inputs = document.querySelectorAll('#p2ps-otp .db-otp__input');
            var pin = '';
            for (var i = 0; i < inputs.length; i++) pin += inputs[i].value;
            return pin;
        }

        function confirmPin() {
            var pin = readOtp();
            state.pin = pin;
            startWithFile(state._file);
        }

        function skipPin() {
            state.pin = '';
            startWithFile(state._file);
        }

        function otpInput(event, idx) {
            var val = event.target.value.replace(/\D/g, '');
            event.target.value = val;
            if (val && idx < 5) {
                var next = document.querySelectorAll('#p2ps-otp .db-otp__input')[idx + 1];
                if (next) next.focus();
            }
        }

        function otpKeydown(event, idx) {
            if (event.key === 'Backspace' && !event.target.value && idx > 0) {
                var prev = document.querySelectorAll('#p2ps-otp .db-otp__input')[idx - 1];
                if (prev) prev.focus();
            }
            if (event.key === 'Enter') confirmPin();
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
            var otpInputs = document.querySelectorAll('#p2ps-otp .db-otp__input');
            for (var i = 0; i < otpInputs.length; i++) otpInputs[i].value = '';
            setPhase('idle');
        }

        function cleanup() {
            var recvIds = Object.keys(state._receivers);
            for (var i = 0; i < recvIds.length; i++) {
                var r = state._receivers[recvIds[i]];
                if (r.sender) r.sender.cancel();
                if (r.pc) r.pc.close();
            }
            state._receivers = {};
            state._receiverIndex = 0;

            if (state._signaling) { state._signaling.close(); state._signaling = null; }
            state._encKey = null;
        }

        // Expose public API
        return {
            onFileSelect: onFileSelect,
            onDragOver: onDragOver,
            onDragLeave: onDragLeave,
            onDrop: onDrop,
            confirmPin: confirmPin,
            skipPin: skipPin,
            otpInput: otpInput,
            otpKeydown: otpKeydown,
            copyLink: copyLink,
            cancel: cancel,
            cancelTransfer: cancelTransfer,
            reset: reset
        };
    })();
})();
