// QuickShare P2P send orchestration — TypeScript port (IIFE, no modules).
// Handles file selection, share link creation, WebRTC signaling, and multi-receiver transfer.
// The sender page stays on the 'waiting' phase (link + QR always visible) while
// receivers connect, receive, and complete independently. Each receiver's status
// is shown in a dynamic list below the QR code.
(function () {
    'use strict';

    interface P2pQuota {
        unlimited?: boolean;
        remaining?: number;
        allowance?: number;
        unlocked?: number;
        canUnlockWithAd?: boolean;
        error?: string;
    }

    interface P2pCreateError extends Error {
        quota?: P2pQuota | null;
    }

    interface P2pReceiverEntry {
        index: number;
        status: string;
        progress: number;
        pc: P2PPeerConnection | null;
        channel: RTCDataChannel | null;
        sender: P2PFileSender | null;
        fileName: string;
        _wasReceiving?: boolean;
        _stallTimer?: number;
    }

    interface P2pShareResponse {
        shareCode: string;
        shareLink?: string;
    }

    const RECEIVER_CONNECT_TIMEOUT_MS = 20000;
    // If no progress event arrives for this long while status is 'receiving',
    // the receiver is gone (tab closed / network dropped). WebRTC state events
    // alone can stay silent for a long time on a dead pipe, so this watchdog
    // guarantees the sender eventually shows a disconnect instead of a frozen
    // progress bar.
    const RECEIVER_STALL_TIMEOUT_MS = 30000;
    const RELAY_TYPE = {
        offer: 'offer',
        answer: 'answer',
        ice: 'ice',
        netProbe: 'netprobe'
    } as const;
    const RECEIVER_STATUS = {
        connecting: 'connecting',
        receiving: 'receiving',
        done: 'done',
        disconnected: 'disconnected',
        differentNetwork: 'different-network'
    } as const;
    function apiBase(): string { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
    function formatBytes(b: number): string { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
    function show(id: string): void { const el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id: string): void { const el = document.getElementById(id); if (el) el.classList.add('hidden'); }
    function text(id: string, val: string): void { const el = document.getElementById(id); if (el) el.textContent = val; }

    // UI copy is rendered by the page into a JSON block — no English literals here.
    const T: Record<string, string> = (function () {
        const el = document.getElementById('p2p-send-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (e) { return {}; }
    })();
    function t(key: string, a?: string | number, b?: string | number): string {
        const v = T[key] || '';
        return v.replace('{0}', String(a)).replace('{1}', String(b));
    }

    window.p2pSend = (function (): P2pSendPage {
        const state: {
            phase: string;
            progress: number;
            shareCode: string;
            shareLink: string;
            error: string;
            _file: File | null;
            _intentionalStop: boolean;
            _signaling: P2PSignalingClient | null;
            _receivers: Record<string, P2pReceiverEntry>;
            _receiverIndex: number;
            _quota: P2pQuota | null;
            _localAddress: string | null;
            _addressProbe: Promise<string | null> | null;
        } = {
            phase: 'idle', progress: 0, shareCode: '', shareLink: '', error: '',
            _file: null, _intentionalStop: false,
            _signaling: null, _receivers: {}, _receiverIndex: 0,
            _quota: null, _localAddress: null, _addressProbe: null
        };
        function setPhase(phase: string): void { state.phase = phase; render(); }

        // --- Daily P2P upload quota ---

        function fetchQuota(): void {
            const api = apiBase();
            fetch(api + '/api/p2p/quota', { headers: { 'Accept': 'application/json' } })
                .then(function (res) { return res.ok ? res.json() : null; })
                .then(function (data) {
                    state._quota = data;
                    renderQuota();
                })
                .catch(function () { /* banner stays hidden on network error */ });
        }

        function renderQuota(): void {
            const el = document.getElementById('p2ps-quota');
            if (!el || !state._quota) return;

            if (state._quota.unlimited) {
                el.classList.add('hidden');
                return;
            }

            const remaining = state._quota.remaining;
            const allowance = state._quota.allowance;
            const unlocked = state._quota.unlocked || 0;
            const suffix = unlocked > 0 ? t('quotaFromAds', unlocked) : '';
            let quotaText = t('quotaLeft', remaining, allowance) + suffix;
            if (remaining != null && remaining <= 0) {
                quotaText = t('quotaLimitReached', allowance, allowance);
                quotaText += state._quota.canUnlockWithAd
                    ? t('quotaUnlockAd')
                    : t('quotaUpgrade');
            }

            el.textContent = quotaText;
            el.classList.toggle('hidden', remaining != null && remaining > 0 && !(state._quota.canUnlockWithAd && unlocked === 0));
            if (remaining != null && remaining <= 0) el.classList.remove('hidden');
        }

        function showAdUnlock(): void {
            const btn = document.getElementById('p2ps-ad-unlock-btn');
            if (!btn || !state._quota || !state._quota.canUnlockWithAd) return;
            btn.classList.remove('hidden');
        }

        function hideAdUnlock(): void {
            const btn = document.getElementById('p2ps-ad-unlock-btn');
            if (btn) btn.classList.add('hidden');
        }

        function onAdRewarded(): void {
            // Ad reward granted (bandwidth +1 P2P upload) — refresh quota and offer retry.
            hideAdUnlock();
            fetchQuota();
            if (state.phase === 'failed' && state._file) {
                setPhase('idle');
                startWithFile(state._file);
            }
        }

        function triggerAdUnlock(): void {
            if (window.QuickShareAds && typeof window.QuickShareAds.showRewardedAd === 'function') {
                window.QuickShareAds.showRewardedAd();
                return;
            }
            // Fallback: scroll to the ad card so the user can click Watch Ad.
            const card = document.getElementById('ad-reward-card');
            if (card) card.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }

        window.addEventListener('quickshare:ad-rewarded', onAdRewarded);
        const unlockBtn = document.getElementById('p2ps-ad-unlock-btn');
        if (unlockBtn) unlockBtn.addEventListener('click', triggerAdUnlock);

        function render(): void {
            const sections = ['p2ps-idle', 'p2ps-creating', 'p2ps-waiting', 'p2ps-failed'];
            sections.forEach(hide);

            const phaseMap: Record<string, string> = { idle: 'p2ps-idle', creating: 'p2ps-creating', waiting: 'p2ps-waiting', failed: 'p2ps-failed' };
            const elId = phaseMap[state.phase];
            if (elId) show(elId);

            if (state.phase === 'waiting') {
                const linkInput = document.getElementById('p2ps-share-link') as HTMLInputElement | null;
                if (linkInput) linkInput.value = state.shareLink;
                const copyBtn = document.getElementById('p2ps-copy-text');
                const copyDone = document.getElementById('p2ps-copy-done');
                if (copyBtn) copyBtn.classList.remove('hidden');
                if (copyDone) copyDone.classList.add('hidden');
                renderQR(state.shareLink);
            }
            if (state.phase === 'failed') {
                text('p2ps-error-msg', state.error || t('transferFailed'));
            }
        }

        function statusLabel(r: P2pReceiverEntry): string {
            if (r.status === RECEIVER_STATUS.connecting) return t('statusConnecting');
            if (r.status === RECEIVER_STATUS.receiving) return t('statusReceiving');
            if (r.status === RECEIVER_STATUS.done) return t('statusDone');
            if (r.status === RECEIVER_STATUS.differentNetwork) return t('statusDifferentNetwork');
            return (r._wasReceiving ? t('statusLeftMid') : t('statusDisconnected'));
        }

        function renderReceivers(): void {
            const container = document.getElementById('p2ps-receiver-list');
            const itemsEl = document.getElementById('p2ps-receiver-items');
            if (!container || !itemsEl) return;

            const ids = Object.keys(state._receivers);
            // The stylesheet keeps the list hidden until a receiver exists, so the
            // visible state must be set explicitly — clearing the inline style
            // would fall back to the stylesheet's display:none.
            container.style.display = ids.length === 0 ? 'none' : 'block';
            if (ids.length === 0) return;

            let html = '';
            for (let i = 0; i < ids.length; i++) {
                const r = state._receivers[ids[i]];
                const pct = Math.round(r.progress * 100);
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

        function onFileSelect(event: Event): void {
            const input = event.target as HTMLInputElement;
            const file = input.files ? input.files[0] : null;
            if (!file) return;
            startWithFile(file);
            input.value = '';
        }

        function onDragOver(event: DragEvent): void {
            event.preventDefault();
            event.stopPropagation();
            hide('idle-h1');
            show('drag-h1');
            hide('idle-sub');
            show('drag-sub');
            hide('dropzone-idle');
            show('dropzone-drag');
            const dz = document.querySelector('.p2p-dropzone');
            if (dz) dz.classList.add('active');
        }

        function onDragLeave(event: DragEvent): void {
            event.preventDefault();
            event.stopPropagation();
            show('idle-h1');
            hide('drag-h1');
            show('idle-sub');
            hide('drag-sub');
            show('dropzone-idle');
            hide('dropzone-drag');
            const dz = document.querySelector('.p2p-dropzone');
            if (dz) dz.classList.remove('active');
        }

        function onDrop(event: DragEvent): void {
            event.preventDefault();
            event.stopPropagation();
            onDragLeave(event);
            const dt = event.dataTransfer;
            const file = dt && dt.files ? dt.files[0] : null;
            if (!file) return;
            startWithFile(file);
        }

        // --- Share creation ---

        function startWithFile(file: File): void {
            state._file = file;
            setPhase('creating');
            hideAdUnlock();

            const body = { FileName: file.name, FileSize: file.size, MimeType: file.type || 'application/octet-stream', BaseUrl: window.location.origin };

            const api = apiBase();
            fetch(api + '/api/shares/p2p', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            })
            .then(function (res) {
                if (!res.ok) return res.text().then(function (raw) {
                    let parsed: P2pQuota | null = null;
                    try { parsed = JSON.parse(raw); } catch (e) { /* not JSON */ }
                    const err = new Error((parsed && (parsed as { message?: string }).message) || raw || t('createFailed')) as P2pCreateError;
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
            .catch(function (err: P2pCreateError) {
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

        function connectSignaling(): void {
            if (state._signaling) {
                state._signaling.close();
                state._signaling = null;
            }
            state._signaling = new window.P2P.SignalingClient(apiBase(), state.shareCode);
            state._signaling.onRegistered = function () {};
            state._signaling.onPeerJoined = function (receiverId: string) { onPeerJoined(receiverId); };
            state._signaling.onPeerLeft = function (message: string, receiverId?: string) {
                onReceiverLeft(receiverId || '');
            };
            state._signaling.onRelay = function (type: string, data: unknown, receiverId?: string) {
                if (type === RELAY_TYPE.answer) onAnswer(data, receiverId || '');
                else if (type === RELAY_TYPE.netProbe) onReceiverNetProbe(data, receiverId || '');
                else if (type === RELAY_TYPE.ice && data && receiverId) {
                    const rcv = state._receivers[receiverId];
                    if (rcv && rcv.pc) rcv.pc.addIceCandidate(data as RTCIceCandidateInit);
                }
            };
            const fileMeta = {
                name: state._file!.name,
                size: state._file!.size,
                mime: state._file!.type || 'application/octet-stream'
            };
            state._signaling.connect('sender', fileMeta);
        }
        // --- Network preflight ---

        // Resolve this device's public IPv4 once and reuse it for every receiver.
        // Resolves to null when it cannot be determined (STUN blocked, IPv6-only),
        // in which case the comparison stays inconclusive and nothing is blocked.
        function localAddress(): Promise<string | null> {
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
        function onReceiverNetProbe(data: unknown, receiverId: string): void {
            const probeData = data as { address?: string } | null | undefined;
            const remoteAddress = (probeData && probeData.address) || null;

            localAddress().then(function (address) {
                if (address && state._signaling) {
                    state._signaling.sendTo(RELAY_TYPE.netProbe, { address: address }, receiverId);
                }

                const result = window.P2P.compareNetwork(address, remoteAddress);
                if (result !== window.P2P.NETWORK_RESULT.different) {
                    return;
                }

                const rcv = state._receivers[receiverId];
                if (!rcv || rcv.status === RECEIVER_STATUS.done) {
                    return;
                }

                closeReceiver(rcv);
                rcv.status = RECEIVER_STATUS.differentNetwork;
                renderReceivers();
            });
        }

        // --- WebRTC ---

        function onPeerJoined(receiverId: string): void {
            if (state.phase === 'idle' || state.phase === 'failed') return;
            if (!window.P2P.PeerConnection) { state.error = t('moduleNotLoaded'); setPhase('failed'); return; }

            // If ICE cannot connect (peers on different networks / NAT), give
            // the receiver a while to establish the connection, then stop
            // showing "Connecting…" forever — the receiver shows its own
            // clear error message.
            setTimeout(function () {
                const pulled = state._receivers[receiverId];
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

            const pc = new window.P2P.PeerConnection();
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

            const rcv = state._receivers[receiverId];
            if (!rcv) return;
            const channel = pc.createDataChannel('filetransfer');
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

        function onAnswer(data: unknown, receiverId: string): void {
            const sdp = (data && (data as { sdp?: unknown }).sdp) || (data as string);
            const rcv = state._receivers[receiverId];
            if (rcv && rcv.pc) {
                rcv.pc.setRemoteSdp(sdp as string).catch(function () {});
            }
        }

        function onChannelOpen(receiverId: string): void {
            if (!window.P2P.FileSender) { state.error = t('senderModuleNotLoaded'); setPhase('failed'); return; }

            const rcv = state._receivers[receiverId];
            if (!rcv) return;
            rcv.status = RECEIVER_STATUS.receiving;
            renderReceivers();

            const channel = rcv.channel;
            const sender = new window.P2P.FileSender(state._file!, channel!, null);
            rcv.sender = sender;

            sender.onProgress = function (sent: number, total: number) {
                const r = state._receivers[receiverId];
                if (!r) return;
                r.progress = sent / total;
                // Re-arm the stall watchdog on every progress event.
                clearTimeout(r._stallTimer);
                r._stallTimer = window.setTimeout(function () { onReceiverStalled(receiverId); }, RECEIVER_STALL_TIMEOUT_MS);
                renderReceivers();
            };
            sender.onComplete = function () {
                const r = state._receivers[receiverId];
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

        function onReceiverStalled(receiverId: string): void {
            const r = state._receivers[receiverId];
            if (!r || r.status !== RECEIVER_STATUS.receiving) return;
            onReceiverLeft(receiverId);
        }

        function closeReceiver(rcv: P2pReceiverEntry): void {
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

        function onReceiverLeft(receiverId: string): void {
            if (!receiverId) return;
            const rcv = state._receivers[receiverId];
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

        function copyLink(): void {
            const input = document.getElementById('p2ps-share-link') as HTMLInputElement | null;
            if (!input) return;
            input.select();
            try { document.execCommand('copy'); } catch (e) { /* ignore */ }
            navigator.clipboard.writeText(input.value).catch(function () { /* execCommand already copied; clipboard API failure is non-fatal */ });
            const copyBtn = document.getElementById('p2ps-copy-text');
            const copyDone = document.getElementById('p2ps-copy-done');
            if (copyBtn) copyBtn.classList.add('hidden');
            if (copyDone) copyDone.classList.remove('hidden');
            setTimeout(function () {
                if (copyBtn) copyBtn.classList.remove('hidden');
                if (copyDone) copyDone.classList.add('hidden');
            }, 2000);
        }

        function cancel(): void { state._intentionalStop = true; cleanup(); setPhase('idle'); }
        function cancelTransfer(): void { state._intentionalStop = true; cleanup(); setPhase('idle'); }

        function renderQR(value: string): void {
            const el = document.getElementById('p2ps-qrcode');
            if (!el) return;
            el.innerHTML = '';
            const qrcode = window.qrcode;
            if (!value || !qrcode) {
                if (!qrcode) el.textContent = t('qrNotLoaded');
                return;
            }
            try {
                const qr = qrcode(0, 'M');
                qr.addData(value);
                qr.make();
                const moduleCount = qr.getModuleCount();
                const cell = 6;
                const dim = moduleCount * cell;
                const parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="' + t('qrAria') + '" style="image-rendering:pixelated"><rect width="' + dim + '" height="' + dim + '" fill="#fff"/>'];
                for (let r = 0; r < moduleCount; r++) {
                    for (let c = 0; c < moduleCount; c++) {
                        if (qr.isDark(r, c)) parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
                    }
                }
                parts.push('</svg>');
                el.innerHTML = parts.join('');
            } catch (e) {
                el.textContent = t('qrUnavailable');
            }
        }

        function reset(): void {
            cleanup();
            state._file = null;
            state.shareCode = '';
            state.shareLink = '';
            const qrEl = document.getElementById('p2ps-qrcode');
            if (qrEl) qrEl.innerHTML = '';
            state.progress = 0;
            state.error = '';
            hideAdUnlock();
            fetchQuota();
            setPhase('idle');
        }

        function cleanup(): void {
            const recvIds = Object.keys(state._receivers);
            for (let i = 0; i < recvIds.length; i++) {
                const r = state._receivers[recvIds[i]];
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
