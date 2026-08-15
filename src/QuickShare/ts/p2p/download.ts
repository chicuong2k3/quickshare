// QuickShare P2P download orchestration — TypeScript port (IIFE, no modules).
// Receiver-side flow: connect to signaling, wait for the sender, then receive
// the file over WebRTC. Includes network preflight and watchdog timeouts so a
// dead sender never leaves a frozen progress bar.
(function () {
    'use strict';

    // UI copy is rendered by the page into a JSON block — no English literals here.
    const T: Record<string, string> = (function () {
        const el = document.getElementById('p2p-download-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    })();
    function t(key: string, a?: string | number, b?: string | number): string {
        const v = T[key] || '';
        return v.replace('{0}', String(a)).replace('{1}', String(b));
    }
    function apiBase(): string { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
    function formatBytes(b: number): string { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
    function show(id: string): void { const el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id: string): void { const el = document.getElementById(id); if (el) el.classList.add('hidden'); }
    function text(id: string, val: string): void { const el = document.getElementById(id); if (el) el.textContent = val; }
    const SIGNALING_CONNECT_TIMEOUT_MS = 15000;
    const SIGNALING_CONNECT_TIMEOUT_MESSAGE = t('signalingTimeout');
    const RECEIVER_HANDSHAKE_TIMEOUT_MS = 25000;
    const RECEIVER_HANDSHAKE_TIMEOUT_MESSAGE = t('handshakeTimeout');
    const ICE_CONNECT_TIMEOUT_MS = 12000;
    const ICE_CONNECT_TIMEOUT_MESSAGE = t('iceTimeout');
    const DIFFERENT_NETWORK_MESSAGE = t('diffNetwork');
    const SENDER_LEFT_MESSAGE = t('senderLeft');
    const RELAY_TYPE = {
        offer: 'offer',
        ice: 'ice',
        answer: 'answer',
        netProbe: 'netprobe'
    } as const;
    // No progress for this long while transferring means the sender is gone
    // without a clean close (network dropped, device slept). WebRTC state
    // events can stay silent on a dead pipe, so this watchdog guarantees the
    // receiver stops showing a frozen progress bar.
    const TRANSFER_STALL_TIMEOUT_MS = 30000;

    window.p2pDownload = (function (): P2pDownloadPage {
        const state: {
            phase: string;
            progress: number;
            fileName: string;
            fileSize: number;
            mimeType: string;
            shareCode: string;
            error: string;
            fallbackUrl: string;
            _receivedBytes: number;
            _startTime: number;
            _signaling: P2PSignalingClient | null;
            _peerConnection: P2PPeerConnection | null;
            _dataChannel: RTCDataChannel | null;
            _receiver: P2PFileReceiver | null;
            _intentionalStop: boolean;
            _pendingOffer: unknown;
            _pendingIceCandidates: Array<RTCIceCandidateInit | string>;
            _isRegistered: boolean;
            _connectTimeoutId: ReturnType<typeof setTimeout> | null;
            _handshakeTimeoutId: ReturnType<typeof setTimeout> | null;
            _iceTimeoutId: ReturnType<typeof setTimeout> | null;
            _stallTimeoutId: ReturnType<typeof setTimeout> | null;
            _localAddress: string | null;
            _remoteAddress: string | null;
            _networkResult: P2PNetworkResult | null;
            _blocked: boolean;
        } = {
            phase: 'checking', progress: 0, fileName: '', fileSize: 0, mimeType: '',
            shareCode: (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.shareCode || ''),
            error: '', fallbackUrl: '',
            _receivedBytes: 0, _startTime: 0,
            _signaling: null, _peerConnection: null, _dataChannel: null, _receiver: null,
            _intentionalStop: false,
            _pendingOffer: null, _pendingIceCandidates: [], _isRegistered: false,
            _connectTimeoutId: null, _handshakeTimeoutId: null, _iceTimeoutId: null,
            _stallTimeoutId: null,
            _localAddress: null, _remoteAddress: null, _networkResult: null, _blocked: false
        };

        function render(): void {
            [{ p: 'checking', id: 'p2pd-checking' }, { p: 'ready', id: 'p2pd-ready' }, { p: 'connecting', id: 'p2pd-ready' }, { p: 'transferring', id: 'p2pd-transfer' }, { p: 'complete', id: 'p2pd-complete' }, { p: 'waiting-sender', id: 'p2pd-waiting' }, { p: 'error', id: 'p2pd-error' }].forEach(function (s) { hide(s.id); });
            const phaseEls: Record<string, string> = {
                'checking': 'p2pd-checking', 'ready': 'p2pd-ready',
                'connecting': 'p2pd-ready', 'transferring': 'p2pd-transfer',
                'complete': 'p2pd-complete', 'waiting-sender': 'p2pd-waiting',
                'error': 'p2pd-error'
            };
            const elId = phaseEls[state.phase];
            if (elId) show(elId);

            if (state.phase === 'ready' || state.phase === 'connecting') {
                text('p2pd-filename', state.fileName);
                text('p2pd-filesize', formatBytes(state.fileSize));
                const btn = document.getElementById('p2pd-dl-btn') as HTMLButtonElement | null;
                const btnTxt = document.getElementById('p2pd-dl-text');
                if (state.phase === 'connecting') {
                    if (btn) btn.disabled = true;
                    if (btnTxt) btnTxt.textContent = t('connecting');
                } else {
                    if (btn) btn.disabled = false;
                    if (btnTxt) btnTxt.textContent = t('downloadBtn');
                }
            }
            if (state.phase === 'transferring') {
                text('p2pd-transfer-name', state.fileName);
                text('p2pd-transfer-progress', progressLabel());
                const bar = document.getElementById('p2pd-progress-bar');
                if (bar) bar.setAttribute('aria-valuenow', String(state.progress));
            }
            if (state.phase === 'complete') text('p2pd-complete-name', state.fileName);
            if (state.phase === 'error') {
                text('p2pd-error-msg', state.error || t('connFailed'));
                show('p2pd-retry');
            }
        }

        function progressLabel(): string {
            if (state.progress === 0) return t('starting');
            let l = state.progress + '%';
            if (state._receivedBytes && state.fileSize) {
                l += ' \u00b7 ' + formatBytes(state._receivedBytes) + ' / ' + formatBytes(state.fileSize);
            }
            const elapsed = (Date.now() - state._startTime) / 1000;
            if (elapsed > 0 && state._receivedBytes > 0) {
                const bps = state._receivedBytes / elapsed;
                l += ' \u00b7 ' + formatBytes(bps) + '/s';
                const rem = (state.fileSize - state._receivedBytes) / bps;
                if (rem < 60) l += ' \u00b7 ~' + Math.round(rem) + 's';
                else l += ' \u00b7 ~' + Math.round(rem / 60) + 'm';
            }
            return l;
        }

        function clearConnectTimeout(): void {
            if (state._connectTimeoutId) {
                clearTimeout(state._connectTimeoutId);
                state._connectTimeoutId = null;
            }
        }

        function armConnectTimeout(): void {
            clearConnectTimeout();
            state._connectTimeoutId = setTimeout(function () {
                if (state._isRegistered || state.phase !== 'checking') {
                    return;
                }

                state.phase = 'error';
                state.error = SIGNALING_CONNECT_TIMEOUT_MESSAGE;
                render();
            }, SIGNALING_CONNECT_TIMEOUT_MS);
        }

        function clearHandshakeTimeout(): void {
            if (state._handshakeTimeoutId) {
                clearTimeout(state._handshakeTimeoutId);
                state._handshakeTimeoutId = null;
            }
        }

        function armHandshakeTimeout(): void {
            clearHandshakeTimeout();
            state._handshakeTimeoutId = setTimeout(function () {
                if (state.phase !== 'connecting') {
                    return;
                }

                state.phase = 'error';
                state.error = RECEIVER_HANDSHAKE_TIMEOUT_MESSAGE;
                render();
            }, RECEIVER_HANDSHAKE_TIMEOUT_MS);
        }

        function clearStallTimeout(): void {
            if (state._stallTimeoutId) {
                clearTimeout(state._stallTimeoutId);
                state._stallTimeoutId = null;
            }
        }

        function armStallTimeout(): void {
            clearStallTimeout();
            state._stallTimeoutId = setTimeout(function () {
                if (state.phase !== 'transferring') {
                    return;
                }

                failTransfer(SENDER_LEFT_MESSAGE);
            }, TRANSFER_STALL_TIMEOUT_MS);
        }

        function clearIceTimeout(): void {
            if (state._iceTimeoutId) {
                clearTimeout(state._iceTimeoutId);
                state._iceTimeoutId = null;
            }
        }

        function armIceTimeout(): void {
            clearIceTimeout();
            state._iceTimeoutId = setTimeout(function () {
                if (state.phase === 'connecting' && !state._dataChannel) {
                    state.phase = 'error';
                    state.error = ICE_CONNECT_TIMEOUT_MESSAGE;
                    render();
                }
            }, ICE_CONNECT_TIMEOUT_MS);
        }

        function failTransfer(message: string): void {
            clearHandshakeTimeout();
            clearIceTimeout();
            clearStallTimeout();
            if (state._receiver) {
                state._receiver.cancel();
                state._receiver = null;
            }
            if (state._peerConnection) {
                state._peerConnection.close();
                state._peerConnection = null;
            }
            state._dataChannel = null;
            state.phase = 'error';
            state.error = message;
            render();
        }

        // --- Network preflight ---

        // Probe the public address, tell the peer, and compare as soon as both
        // sides are known. A confirmed mismatch fails immediately instead of
        // making the user click Download and wait out the ICE timeout.
        function startNetworkProbe(): void {
            if (!window.P2P || typeof window.P2P.probePublicAddress !== 'function') {
                return;
            }

            window.P2P.probePublicAddress().then(function (address) {
                state._localAddress = address;
                if (address && state._signaling) {
                    state._signaling.send(RELAY_TYPE.netProbe, { address: address });
                }
                evaluateNetwork();
            });
        }

        function evaluateNetwork(): void {
            if (state._blocked) {
                return;
            }

            state._networkResult = window.P2P.compareNetwork(state._localAddress, state._remoteAddress);
            if (state._networkResult !== window.P2P.NETWORK_RESULT.different) {
                return;
            }

            state._blocked = true;
            failTransfer(DIFFERENT_NETWORK_MESSAGE);
        }

        function init(): void {
            render();
            const api = apiBase();
            if (!window.P2P || !window.P2P.SignalingClient) {
                state.phase = 'error';
                state.error = t('signalingNotLoaded');
                render();
                return;
            }
            state._signaling = new window.P2P.SignalingClient(api, state.shareCode);
            state._signaling.onRegistered = function () {
                state._isRegistered = true;
                clearConnectTimeout();
                if (state.phase === 'checking') {
                    state.phase = 'waiting-sender';
                    render();
                }
            };
            state._signaling.onFileMeta = function (meta: P2PReceivedMeta) {
                state.fileName = meta.fileName;
                state.fileSize = meta.fileSize;
                state.mimeType = meta.mimeType;
                clearConnectTimeout();
                if (state.phase !== 'ready'
                    && state.phase !== 'connecting'
                    && state.phase !== 'transferring'
                    && state.phase !== 'complete') {
                    state.phase = 'ready';
                }
                render();
            };
            state._signaling.onPeerJoined = function () {
                clearConnectTimeout();
                if (state.phase === 'waiting-sender' || state.phase === 'checking') {
                    state.phase = 'ready';
                    render();
                }
            };
            state._signaling.onWaiting = function () {
                clearConnectTimeout();
                state.phase = 'waiting-sender';
                render();
            };
            state._signaling.onPeerLeft = function () {
                clearHandshakeTimeout();
                // A sender that leaves mid-transfer is a failure, not a fresh
                // wait: the file is incomplete and reopening the link cannot
                // resume it. Only pre-transfer phases fall back to waiting.
                if (state.phase === 'transferring') {
                    failTransfer(SENDER_LEFT_MESSAGE);
                    return;
                }
                if (state.phase === 'connecting' || state.phase === 'ready') {
                    state.phase = 'waiting-sender';
                    render();
                }
            };
            state._signaling.onRelay = function (type: string, data: unknown) {
                if (type === RELAY_TYPE.netProbe) {
                    const probeData = data as { address?: string } | null | undefined;
                    state._remoteAddress = (probeData && probeData.address) || null;
                    evaluateNetwork();
                    return;
                }
                if (type === RELAY_TYPE.offer) {
                    if (state._peerConnection) {
                        handleOffer(data);
                    } else {
                        state._pendingOffer = data;
                    }
                } else if (type === RELAY_TYPE.ice && data) {
                    const candidate = (data as { candidate?: RTCIceCandidateInit | string }).candidate || (data as RTCIceCandidateInit | string);
                    if (state._peerConnection) {
                        state._peerConnection.addIceCandidate(candidate);
                    } else {
                        state._pendingIceCandidates.push(candidate);
                    }
                }
            };
            state._signaling.onError = function (message: string) {
                if (state._intentionalStop) {
                    return;
                }

                clearConnectTimeout();
                clearHandshakeTimeout();
                state.phase = 'error';
                state.error = t('connFailed');
                console.warn('P2P signaling error:', message);
            };
            armConnectTimeout();
            state._signaling.connect('receiver');
            startNetworkProbe();
        }

        function startDownload(): void {
            if (state._blocked) {
                return;
            }
            state.phase = 'connecting';
            armHandshakeTimeout();
            armIceTimeout();
            render();
            if (!window.P2P.PeerConnection || !window.P2P.FileReceiver) {
                state.phase = 'error';
                state.error = t('modulesNotLoaded');
                render();
                return;
            }
            state._peerConnection = new window.P2P.PeerConnection();
            state._peerConnection.onDataChannel = function (channel) { clearIceTimeout(); onDataChannel(channel); };
            state._peerConnection.onIceCandidate = function (c) {
                if (state._signaling) state._signaling.send(RELAY_TYPE.ice, c);
            };
            state._peerConnection.onConnectionState = function (s) {
                if (s !== 'failed' && s !== 'disconnected') {
                    return;
                }
                // Dropping during transfer means the peer went away; dropping
                // while still connecting means ICE never got through.
                if (state.phase === 'transferring') {
                    failTransfer(SENDER_LEFT_MESSAGE);
                } else if (state.phase === 'connecting') {
                    failTransfer(ICE_CONNECT_TIMEOUT_MESSAGE);
                }
            };
            // Process any offer/ICE that arrived before user clicked download
            if (state._pendingOffer) {
                handleOffer(state._pendingOffer);
                state._pendingOffer = null;
            }
            for (let i = 0; i < state._pendingIceCandidates.length; i++) {
                const pending = state._pendingIceCandidates[i];
                const pendingCandidate = (pending as { candidate?: RTCIceCandidateInit | string }).candidate || pending;
                state._peerConnection.addIceCandidate(pendingCandidate);
            }
            state._pendingIceCandidates = [];
        }

        function handleOffer(data: unknown): void {
            let sdp: unknown = (data as { sdp?: unknown }).sdp || (data as string);
            if (sdp && typeof sdp === 'object' && (sdp as { sdp?: string }).sdp) {
                sdp = (sdp as { sdp?: string }).sdp;
            }
            if (!state._peerConnection) {
                return;
            }
            state._peerConnection.createAnswer(sdp as string).then(function (answerSdp) {
                if (state._signaling) {
                    state._signaling.send(RELAY_TYPE.answer, { sdp: answerSdp });
                }
            }).catch(function () {
                state.phase = 'error';
                state.error = t('offerFailed');
                render();
            });
        }

        function onDataChannel(channel: RTCDataChannel): void {
            state._dataChannel = channel;
            state._receiver = new window.P2P.FileReceiver(channel, null);
            state._startTime = Date.now();
            state._receiver.onFileInfo = function (info: P2PFileMeta) {
                state.fileName = info.name;
                state.fileSize = info.size;
                state.mimeType = info.mime;
            };
            state._receiver.onProgress = function (rec: number, tot: number) {
                state.progress = Math.round((rec / tot) * 100);
                state._receivedBytes = rec;
                armStallTimeout();
                render();
            };
            state._receiver.onComplete = function () {
                clearHandshakeTimeout();
                clearStallTimeout();
                state.progress = 100;
                state.phase = 'complete';
                render();
            };
            state._receiver.onError = function (msg: string) {
                console.warn('P2P receive error:', msg);
                failTransfer(t('transferFailed'));
            };
            clearHandshakeTimeout();
            state.phase = 'transferring';
            armStallTimeout();
            state._receiver.start();
            render();
        }

        function cancelDownload(): void {
            state._intentionalStop = true;
            clearConnectTimeout();
            clearHandshakeTimeout();
            clearStallTimeout();
            if (state._receiver) state._receiver.cancel();
            cleanup();
            state.phase = 'checking';
            state._intentionalStop = false;
            render();
        }

        function cleanup(): void {
            clearConnectTimeout();
            clearHandshakeTimeout();
            clearIceTimeout();
            clearStallTimeout();
            if (state._signaling) {
                state._signaling.close();
                state._signaling = null;
            }
            if (state._peerConnection) {
                state._peerConnection.close();
                state._peerConnection = null;
            }
            if (state._receiver) {
                state._receiver.cancel();
                state._receiver = null;
            }
            state._dataChannel = null;
        }

        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
        return { startDownload: startDownload, cancelDownload: cancelDownload };
    })();
})();
