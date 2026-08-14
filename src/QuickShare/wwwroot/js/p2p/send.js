// QuickShare P2P send orchestration — vanilla JS
// Handles file selection, share link creation, WebRTC signaling, and multi-receiver transfer.
// The sender page stays on the 'waiting' phase (link + QR always visible) while
// receivers connect, receive, and complete independently. Each receiver's status
// is shown in a dynamic list below the QR code.
(function () {
    'use strict';
    var RECEIVER_CONNECT_TIMEOUT_MS = 20000;
    // If no progress event arrives for this long while status is 'receiving',
    // the receiver is gone (tab closed / network dropped). WebRTC state events
    // alone can stay silent for a long time on a dead pipe, so this watchdog
    // guarantees the sender eventually shows a disconnect instead of a frozen
    // progress bar.
    var RECEIVER_STALL_TIMEOUT_MS = 30000;
    var RELAY_TYPE = {
        offer: 'offer',
        answer: 'answer',
        ice: 'ice',
        netProbe: 'netprobe'
    };
    var RECEIVER_STATUS = {
        connecting: 'connecting',
        receiving: 'receiving',
        done: 'done',
        disconnected: 'disconnected',
        differentNetwork: 'different-network'
    };
    function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
    function formatBytes(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
    function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }
    function text(id, val) { var el = document.getElementById(id); if (el) el.textContent = val; }

    // UI copy is rendered by the page into a JSON block — no English literals here.
    var T = (function () {
        var el = document.getElementById('p2p-send-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    })();
    function t(key, a, b) {
        var v = T[key] || '';
        return v.replace('{0}', a).replace('{1}', b);
    }

    window.p2pSend = (function () {
        var state = {
            phase: 'idle', progress: 0, shareCode: '', shareLink: '', error: '',
            _file: null, _intentionalStop: false,
            _signaling: null, _receivers: {}, _receiverIndex: 0,
            _quota: null, _localAddress: null, _addressProbe: null
        };
        function setPhase(phase) { state.phase = phase; render(); }

        // --- Daily P2P upload quota ---

        function fetchQuota() {
            var api = apiBase();
            fetch(api + '/api/p2p/quota', { headers: { 'Accept': 'application/json' } })
                .then(function (res) { return res.ok ? res.json() : null; })
                .then(function (data) {
                    state._quota = data;
                    renderQuota();
                })
                .catch(function () { /* banner stays hidden on network error */ });
        }

        function renderQuota() {
            var el = document.getElementById('p2ps-quota');
            if (!el || !state._quota) return;

            if (state._quota.unlimited) {
                el.classList.add('hidden');
                return;
            }

            var remaining = state._quota.remaining;
            var allowance = state._quota.allowance;
            var unlocked = state._quota.unlocked || 0;
            var suffix = unlocked > 0 ? t('quotaFromAds', unlocked) : '';
            var text = t('quotaLeft', remaining, allowance) + suffix;
            if (remaining <= 0) {
                text = t('quotaLimitReached', allowance, allowance);
                text += state._quota.canUnlockWithAd
                    ? t('quotaUnlockAd')
                    : t('quotaUpgrade');
            }

            el.textContent = text;
            el.classList.toggle('hidden', remaining > 0 && !(state._quota.canUnlockWithAd && unlocked === 0));
            if (remaining <= 0) el.classList.remove('hidden');
        }

        function showAdUnlock() {
            var btn = document.getElementById('p2ps-ad-unlock-btn');
            if (!btn || !state._quota || !state._quota.canUnlockWithAd) return;
            btn.classList.remove('hidden');
        }

        function hideAdUnlock() {
            var btn = document.getElementById('p2ps-ad-unlock-btn');
            if (btn) btn.classList.add('hidden');
        }

        function onAdRewarded() {
            // Ad reward granted (bandwidth +1 P2P upload) — refresh quota and offer retry.
            hideAdUnlock();
            fetchQuota();
            if (state.phase === 'failed' && state._file) {
                setPhase('idle');
                startWithFile(state._file);
            }
        }

        function triggerAdUnlock() {
            if (window.QuickShareAds && typeof window.QuickShareAds.showRewardedAd === 'function') {
                window.QuickShareAds.showRewardedAd();
                return;
            }
            // Fallback: scroll to the ad card so the user can click Watch Ad.
            var card = document.getElementById('ad-reward-card');
            if (card) card.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }

        window.addEventListener('quickshare:ad-rewarded', onAdRewarded);
        var unlockBtn = document.getElementById('p2ps-ad-unlock-btn');
        if (unlockBtn) unlockBtn.addEventListener('click', triggerAdUnlock);

        function render() {
            var sections = ['p2ps-idle', 'p2ps-creating', 'p2ps-waiting', 'p2ps-failed'];
            sections.forEach(hide);

            var phaseMap = { idle: 'p2ps-idle', creating: 'p2ps-creating', waiting: 'p2ps-waiting', failed: 'p2ps-failed' };
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
            if (state.phase === 'failed') {
                text('p2ps-error-msg', state.error || t('transferFailed'));
            }
        }

        function statusLabel(r) {
            if (r.status === RECEIVER_STATUS.connecting) return t('statusConnecting');
            if (r.status === RECEIVER_STATUS.receiving) return t('statusReceiving');
            if (r.status === RECEIVER_STATUS.done) return t('statusDone');
            if (r.status === RECEIVER_STATUS.differentNetwork) return t('statusDifferentNetwork');
            return (r._wasReceiving ? t('statusLeftMid') : t('statusDisconnected'));
        }

        function renderReceivers() {
            var container = document.getElementById('p2ps-receiver-list');
            var itemsEl = document.getElementById('p2ps-receiver-items');
            if (!container || !itemsEl) return;

            var ids = Object.keys(state._receivers);
            // The stylesheet keeps the list hidden until a receiver exists, so the
            // visible state must be set explicitly \u2014 clearing the inline style
            // would fall back to the stylesheet's display:none.
            container.style.display = ids.length === 0 ? 'none' : 'block';
            if (ids.length === 0) return;

            var html = '';
            for (var i = 0; i < ids.length; i++) {
                var r = state._receivers[ids[i]];
                var pct = Math.round(r.progress * 100);
                html += '<div class="p2p-receiver-entry">';
                html += '<span class="p2p-receiver-index">#' + r.index + '</span>';
                html += '<span class="p2p-receiver-id">' + ids[i].slice(0, 6) + '\u2026</span>';
                html += '<span class="p2p-receiver-status p2p-receiver-status--' + r.status + '">' + statusLabel(r) + '</span>';
                if (r.status === RECEIVER_STATUS.receiving) {
                    html += '<div class="p2p-receiver-progress"><div class="p2p-receiver-progress-fill" style="width:' + pct + '%"></div></div>';
                    html += '<span class="p2p-receiver-pct">' + pct + '%</span>';
                }
                if (r.status === RECEIVER_STATUS.done) {
                    html += '<span class="p2p-receiver-size">' + formatBytes(state._file ? state._file.size : 0) + '</span>';
                }
                html += '</div>';
            }
            itemsEl.innerHTML = html;
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
            startWithFile(file);
        }

        // --- Share creation ---

        function startWithFile(file) {
            state._file = file;
            setPhase('creating');
            hideAdUnlock();

            var body = { FileName: file.name, FileSize: file.size, MimeType: file.type || 'application/octet-stream', BaseUrl: window.location.origin };

            var api = apiBase();
            fetch(api + '/api/shares/p2p', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            })
            .then(function (res) {
                if (!res.ok) return res.text().then(function (raw) {
                    var parsed = null;
                    try { parsed = JSON.parse(raw); } catch (e) { /* not JSON */ }
                    var err = new Error((parsed && parsed.message) || raw || t('createFailed'));
                    err.quota = parsed;
                    throw err;
                });
                return res.json();
            })
            .then(function (data) {
                state.shareCode = data.shareCode;
                state.shareLink = data.shareLink || (window.location.origin + '/p2p/d/' + data.shareCode);
                state.phase = 'waiting';
                render();
                fetchQuota();
                connectSignaling();
            })
            .catch(function (err) {
                state.error = err.message || t('createLinkFailed');
                if (err.quota && err.quota.error === 'daily_p2p_upload_limit_reached') {
                    state._quota = err.quota;
                    renderQuota();
                    showAdUnlock();
                }
                setPhase('failed');
            });
        }

        // --- Signaling ---

        function connectSignaling() {
            if (state._signaling) {
                state._signaling.close();
                state._signaling = null;
            }
            state._signaling = new window.P2P.SignalingClient(apiBase(), state.shareCode);
            state._signaling.onRegistered = function () {};
            state._signaling.onPeerJoined = function (receiverId) { onPeerJoined(receiverId); };
            state._signaling.onPeerLeft = function (message, receiverId) {
                onReceiverLeft(receiverId);
            };
            state._signaling.onRelay = function (type, data, receiverId) {
                if (type === RELAY_TYPE.answer) onAnswer(data, receiverId);
                else if (type === RELAY_TYPE.netProbe) onReceiverNetProbe(data, receiverId);
                else if (type === RELAY_TYPE.ice && data && receiverId) {
                    var rcv = state._receivers[receiverId];
                    if (rcv && rcv.pc) rcv.pc.addIceCandidate(data);
                }
            };
            var fileMeta = {
                name: state._file.name,
                size: state._file.size,
                mime: state._file.type || 'application/octet-stream'
            };
            state._signaling.connect('sender', fileMeta);
        }
        // --- Network preflight ---

        // Resolve this device's public IPv4 once and reuse it for every receiver.
        // Resolves to null when it cannot be determined (STUN blocked, IPv6-only),
        // in which case the comparison stays inconclusive and nothing is blocked.
        function localAddress() {
            if (state._addressProbe) {
                return state._addressProbe;
            }

            if (!window.P2P || typeof window.P2P.probePublicAddress !== 'function') {
                state._addressProbe = Promise.resolve(null);
                return state._addressProbe;
            }

            state._addressProbe = window.P2P.probePublicAddress().then(function (address) {
                state._localAddress = address;
                return address;
            });
            return state._addressProbe;
        }

        // The receiver announces its public address; answer with ours so both
        // sides reach the same verdict, and mark a confirmed mismatch as such
        // rather than leaving the entry on "Connecting…" until it times out.
        function onReceiverNetProbe(data, receiverId) {
            var remoteAddress = (data && data.address) || null;

            localAddress().then(function (address) {
                if (address && state._signaling) {
                    state._signaling.sendTo(RELAY_TYPE.netProbe, { address: address }, receiverId);
                }

                var result = window.P2P.compareNetwork(address, remoteAddress);
                if (result !== window.P2P.NETWORK_RESULT.different) {
                    return;
                }

                var rcv = state._receivers[receiverId];
                if (!rcv || rcv.status === RECEIVER_STATUS.done) {
                    return;
                }

                closeReceiver(rcv);
                rcv.status = RECEIVER_STATUS.differentNetwork;
                renderReceivers();
            });
        }

        // --- WebRTC ---

        function onPeerJoined(receiverId) {
            if (state.phase === 'idle' || state.phase === 'failed') return;
            if (!window.P2P.PeerConnection) { state.error = t('moduleNotLoaded'); setPhase('failed'); return; }

            // If ICE cannot connect (peers on different networks / NAT), give
            // the receiver a while to establish the connection, then stop
            // showing "Connecting\u2026" forever — the receiver shows its own
            // clear error message.
            setTimeout(function () {
                var pulled = state._receivers[receiverId];
                if (pulled && pulled.status === RECEIVER_STATUS.connecting && !pulled.channel) {
                    onReceiverLeft(receiverId);
                }
            }, RECEIVER_CONNECT_TIMEOUT_MS);

            state._receiverIndex++;
            state._receivers[receiverId] = {
                index: state._receiverIndex,
                status: RECEIVER_STATUS.connecting,
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
                    state._signaling.sendTo(RELAY_TYPE.ice, c, receiverId);
                }
            };
            pc.onConnectionState = function (s) {
                if (s === 'failed' || s === 'disconnected') {
                    onReceiverLeft(receiverId);
                }
            };

            var rcv = state._receivers[receiverId];
            if (!rcv) return;
            var channel = pc.createDataChannel('filetransfer');
            if (!channel) { onReceiverLeft(receiverId); return; }
            rcv.channel = channel;
            channel.onopen = function () { onChannelOpen(receiverId); };
            channel.onerror = function () { onReceiverLeft(receiverId); };
            channel.onclose = function () { onReceiverLeft(receiverId); };

            pc.createOffer()
            .then(function (sdp) {
                if (state._signaling) {
                    state._signaling.sendTo(RELAY_TYPE.offer, { sdp: sdp }, receiverId);
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
            if (!window.P2P.FileSender) { state.error = t('senderModuleNotLoaded'); setPhase('failed'); return; }

            var rcv = state._receivers[receiverId];
            if (!rcv) return;
            rcv.status = RECEIVER_STATUS.receiving;
            renderReceivers();

            var channel = rcv.channel;
            var sender = new window.P2P.FileSender(state._file, channel, null);
            rcv.sender = sender;

            sender.onProgress = function (sent, total) {
                var r = state._receivers[receiverId];
                if (!r) return;
                r.progress = sent / total;
                // Re-arm the stall watchdog on every progress event.
                clearTimeout(r._stallTimer);
                r._stallTimer = window.setTimeout(function () { onReceiverStalled(receiverId); }, RECEIVER_STALL_TIMEOUT_MS);
                renderReceivers();
            };
            sender.onComplete = function () {
                var r = state._receivers[receiverId];
                if (!r) return;
                clearTimeout(r._stallTimer);
                r.status = RECEIVER_STATUS.done;
                r.sender = null;
                r.channel = null;
                renderReceivers();
            };
            sender.onError = function () {
                onReceiverLeft(receiverId);
            };

            sender.start();
        }

        function onReceiverStalled(receiverId) {
            var r = state._receivers[receiverId];
            if (!r || r.status !== RECEIVER_STATUS.receiving) return;
            onReceiverLeft(receiverId);
        }

        function closeReceiver(rcv) {
            clearTimeout(rcv._stallTimer);
            if (rcv.sender) {
                rcv.sender.cancel();
                rcv.sender = null;
            }
            rcv.channel = null;
            if (rcv.pc) {
                rcv.pc.close();
                rcv.pc = null;
            }
        }

        function onReceiverLeft(receiverId) {
            if (!receiverId) return;
            var rcv = state._receivers[receiverId];
            if (!rcv) return;
            if (rcv.status === RECEIVER_STATUS.done
                || rcv.status === RECEIVER_STATUS.disconnected
                || rcv.status === RECEIVER_STATUS.differentNetwork) {
                return;
            }

            rcv._wasReceiving = rcv.status === RECEIVER_STATUS.receiving;
            closeReceiver(rcv);

            rcv.status = RECEIVER_STATUS.disconnected;
            renderReceivers();
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
                if (!window.qrcode) el.textContent = t('qrNotLoaded');
                return;
            }
            try {
                var qr = window.qrcode(0, 'M');
                qr.addData(text);
                qr.make();
                var moduleCount = qr.getModuleCount();
                var cell = 6;
                var dim = moduleCount * cell;
                var parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="' + t('qrAria') + '" style="image-rendering:pixelated"><rect width="' + dim + '" height="' + dim + '" fill="#fff"/>'];
                for (var r = 0; r < moduleCount; r++) {
                    for (var c = 0; c < moduleCount; c++) {
                        if (qr.isDark(r, c)) parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
                    }
                }
                parts.push('</svg>');
                el.innerHTML = parts.join('');
            } catch (e) {
                el.textContent = t('qrUnavailable');
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
            hideAdUnlock();
            fetchQuota();
            setPhase('idle');
        }

        function cleanup() {
            var recvIds = Object.keys(state._receivers);
            for (var i = 0; i < recvIds.length; i++) {
                var r = state._receivers[recvIds[i]];
                if (r.sender) r.sender.cancel();
                if (r.pc) r.pc.close();
                clearTimeout(r._stallTimer);
            }
            state._receivers = {};
            state._receiverIndex = 0;
            renderReceivers();

            if (state._signaling) { state._signaling.close(); state._signaling = null; }
        }

        // Init: load quota banner once the page is ready
        fetchQuota();

        // Expose public API
        return {
            onFileSelect: onFileSelect,
            onDragOver: onDragOver,
            onDragLeave: onDragLeave,
            onDrop: onDrop,
            copyLink: copyLink,
            cancel: cancel,
            cancelTransfer: cancelTransfer,
            reset: reset,
            fetchQuota: fetchQuota
        };
    })();
})();
