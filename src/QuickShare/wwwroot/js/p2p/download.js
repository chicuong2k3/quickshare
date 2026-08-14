(function () {
        'use strict';
        // UI copy is rendered by the page into a JSON block — no English literals here.
        var T = (function () {
            var el = document.getElementById('p2p-download-i18n');
            try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
        })();
        function t(key, a, b) {
            var v = T[key] || '';
            return v.replace('{0}', a).replace('{1}', b);
        }
        function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
        function formatBytes(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
        function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
        function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }
        function text(id, val) { var el = document.getElementById(id); if (el) el.textContent = val; }
        var SIGNALING_CONNECT_TIMEOUT_MS = 15000;
        var SIGNALING_CONNECT_TIMEOUT_MESSAGE = t('signalingTimeout');
        var RECEIVER_HANDSHAKE_TIMEOUT_MS = 25000;
        var RECEIVER_HANDSHAKE_TIMEOUT_MESSAGE = t('handshakeTimeout');
        var ICE_CONNECT_TIMEOUT_MS = 12000;
        var ICE_CONNECT_TIMEOUT_MESSAGE = t('iceTimeout');
        var DIFFERENT_NETWORK_MESSAGE = t('diffNetwork');
        var SENDER_LEFT_MESSAGE = t('senderLeft');
        var RELAY_TYPE = {
            offer: 'offer',
            ice: 'ice',
            answer: 'answer',
            netProbe: 'netprobe'
        };
        // No progress for this long while transferring means the sender is gone
        // without a clean close (network dropped, device slept). WebRTC state
        // events can stay silent on a dead pipe, so this watchdog guarantees the
        // receiver stops showing a frozen progress bar.
        var TRANSFER_STALL_TIMEOUT_MS = 30000;

        window.p2pDownload = (function () {
            var state = {
                phase: 'checking', progress: 0, fileName: '', fileSize: 0, mimeType: '',
                shareCode: (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.shareCode || ''), error: '', fallbackUrl: '',
                _receivedBytes: 0, _startTime: 0,
                _signaling: null, _peerConnection: null, _dataChannel: null, _receiver: null, _intentionalStop: false,
                _pendingOffer: null, _pendingIceCandidates: [], _isRegistered: false, _connectTimeoutId: null,
                _handshakeTimeoutId: null, _iceTimeoutId: null, _stallTimeoutId: null,
                _localAddress: null, _remoteAddress: null, _networkResult: null, _blocked: false
            };

            function render() {
                [{p:'checking',id:'p2pd-checking'},{p:'ready',id:'p2pd-ready'},{p:'connecting',id:'p2pd-ready'},{p:'transferring',id:'p2pd-transfer'},{p:'complete',id:'p2pd-complete'},{p:'waiting-sender',id:'p2pd-waiting'},{p:'error',id:'p2pd-error'}].forEach(function(s){hide(s.id);});
                var phaseEls = {
                    'checking': 'p2pd-checking', 'ready': 'p2pd-ready',
                    'connecting': 'p2pd-ready', 'transferring': 'p2pd-transfer', 'complete': 'p2pd-complete',
                    'waiting-sender': 'p2pd-waiting', 'error': 'p2pd-error'
                };
                var elId = phaseEls[state.phase]; if (elId) show(elId);

                if (state.phase === 'ready' || state.phase === 'connecting') {
                    text('p2pd-filename', state.fileName);
                    text('p2pd-filesize', formatBytes(state.fileSize));
                    var btn = document.getElementById('p2pd-dl-btn');
                    var btnTxt = document.getElementById('p2pd-dl-text');
                    if (state.phase === 'connecting') { if (btn) btn.disabled = true; if (btnTxt) btnTxt.textContent = t('connecting'); }
                    else { if (btn) btn.disabled = false; if (btnTxt) btnTxt.textContent = t('downloadBtn'); }
                }
                if (state.phase === 'transferring') {
                    text('p2pd-transfer-name', state.fileName);
                    text('p2pd-transfer-progress', progressLabel());
                    
            document.getElementById('p2pd-progress-bar').setAttribute('aria-valuenow', state.progress);
                }
                if (state.phase === 'complete') text('p2pd-complete-name', state.fileName);
                if (state.phase === 'error') {
                    text('p2pd-error-msg', state.error || t('connFailed'));
                    show('p2pd-retry');
                }
            }

            function progressLabel() {
                if (state.progress === 0) return t('starting');
                var l = state.progress + '%';
                if (state._receivedBytes && state.fileSize) l += ' \u00b7 ' + formatBytes(state._receivedBytes) + ' / ' + formatBytes(state.fileSize);
                var elapsed = (Date.now() - state._startTime) / 1000;
                if (elapsed > 0 && state._receivedBytes > 0) {
                    var bps = state._receivedBytes / elapsed; l += ' \u00b7 ' + formatBytes(bps) + '/s';
                    var rem = (state.fileSize - state._receivedBytes) / bps;
                    if (rem < 60) l += ' \u00b7 ~' + Math.round(rem) + 's'; else l += ' \u00b7 ~' + Math.round(rem / 60) + 'm';
                }
                return l;
            }
            function clearConnectTimeout() {
                if (state._connectTimeoutId) {
                    clearTimeout(state._connectTimeoutId);
                    state._connectTimeoutId = null;
                }
            }

            function armConnectTimeout() {
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
            function clearHandshakeTimeout() {
                if (state._handshakeTimeoutId) {
                    clearTimeout(state._handshakeTimeoutId);
                    state._handshakeTimeoutId = null;
                }
            }

            function armHandshakeTimeout() {
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
            function clearStallTimeout() {
                if (state._stallTimeoutId) {
                    clearTimeout(state._stallTimeoutId);
                    state._stallTimeoutId = null;
                }
            }

            function armStallTimeout() {
                clearStallTimeout();
                state._stallTimeoutId = setTimeout(function () {
                    if (state.phase !== 'transferring') {
                        return;
                    }

                    failTransfer(SENDER_LEFT_MESSAGE);
                }, TRANSFER_STALL_TIMEOUT_MS);
            }

            function failTransfer(message) {
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
            function startNetworkProbe() {
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

            function evaluateNetwork() {
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

            function clearIceTimeout() {
                if (state._iceTimeoutId) {
                    clearTimeout(state._iceTimeoutId);
                    state._iceTimeoutId = null;
                }
            }
            function armIceTimeout() {
                clearIceTimeout();
                state._iceTimeoutId = setTimeout(function () {
                    if (state.phase === 'connecting' && !state._dataChannel) {
                        state.phase = 'error';
                        state.error = ICE_CONNECT_TIMEOUT_MESSAGE;
                        render();
                    }
                }, ICE_CONNECT_TIMEOUT_MS);
            }



            function init() {
                render();
                var api = apiBase();
                if (!window.P2P || !window.P2P.SignalingClient) { state.phase = 'error'; state.error = t('signalingNotLoaded'); render(); return; }
                state._signaling = new window.P2P.SignalingClient(api, state.shareCode);
                state._signaling.onRegistered = function () {
                    state._isRegistered = true;
                    clearConnectTimeout();
                    if (state.phase === 'checking') {
                        state.phase = 'waiting-sender';
                        render();
                    }
                };
                state._signaling.onFileMeta = function (meta) { state.fileName = meta.fileName; state.fileSize = meta.fileSize; state.mimeType = meta.mimeType; clearConnectTimeout(); if (state.phase !== 'ready' && state.phase !== 'connecting' && state.phase !== 'transferring' && state.phase !== 'complete') { state.phase = 'ready'; } render(); };
                state._signaling.onPeerJoined = function () { clearConnectTimeout(); if (state.phase === 'waiting-sender' || state.phase === 'checking') { state.phase = 'ready'; render(); } };
                state._signaling.onWaiting = function () { clearConnectTimeout(); state.phase = 'waiting-sender'; render(); };
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
                state._signaling.onRelay = function (type, data) {
                    if (type === RELAY_TYPE.netProbe) {
                        state._remoteAddress = (data && data.address) || null;
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
                        var candidate = data.candidate || data;
                        if (state._peerConnection) {
                            state._peerConnection.addIceCandidate(candidate);
                        } else {
                            state._pendingIceCandidates.push(candidate);
                        }
                    }
                };
                state._signaling.onError = function (message) {
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

            function startDownload() {
                if (state._blocked) {
                    return;
                }
                state.phase = 'connecting'; armHandshakeTimeout(); armIceTimeout(); render();
                if (!window.P2P.PeerConnection || !window.P2P.FileReceiver) { state.phase = 'error'; state.error = t('modulesNotLoaded'); render(); return; }
                state._peerConnection = new window.P2P.PeerConnection();
                state._peerConnection.onDataChannel = function (channel) { clearIceTimeout(); onDataChannel(channel); };
                state._peerConnection.onIceCandidate = function (c) { if (state._signaling) state._signaling.send(RELAY_TYPE.ice, c); };
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
                for (var i = 0; i < state._pendingIceCandidates.length; i++) {
                    var pendingCandidate = state._pendingIceCandidates[i].candidate || state._pendingIceCandidates[i];
                    state._peerConnection.addIceCandidate(pendingCandidate);
                }
                state._pendingIceCandidates = [];
            }

            function handleOffer(data) {
                var sdp = data.sdp || data;
                if (sdp && typeof sdp === 'object' && sdp.sdp) sdp = sdp.sdp;
                state._peerConnection.createAnswer(sdp).then(function (answerSdp) { state._signaling.send(RELAY_TYPE.answer, { sdp: answerSdp }); }).catch(function () { state.phase = 'error'; state.error = t('offerFailed'); render(); });
            }

            function onDataChannel(channel) {
                state._dataChannel = channel;
                state._receiver = new window.P2P.FileReceiver(channel, null);
                state._startTime = Date.now();
                state._receiver.onFileInfo = function (info) { state.fileName = info.name; state.fileSize = info.size; state.mimeType = info.mime; };
                state._receiver.onProgress = function (rec, tot) { state.progress = Math.round((rec / tot) * 100); state._receivedBytes = rec; armStallTimeout(); render(); };
                state._receiver.onComplete = function () { clearHandshakeTimeout(); clearStallTimeout(); state.progress = 100; state.phase = 'complete'; render(); };
                state._receiver.onError = function (msg) { console.warn('P2P receive error:', msg); failTransfer(t('transferFailed')); };
                clearHandshakeTimeout();
                state.phase = 'transferring'; armStallTimeout(); state._receiver.start(); render();
            }

            function cancelDownload() { state._intentionalStop = true; clearConnectTimeout(); clearHandshakeTimeout(); clearStallTimeout(); if (state._receiver) state._receiver.cancel(); cleanup(); state.phase = 'checking'; state._intentionalStop = false; render(); }
            function cleanup() { clearConnectTimeout(); clearHandshakeTimeout(); clearIceTimeout(); clearStallTimeout(); if (state._signaling) { state._signaling.close(); state._signaling = null; } if (state._peerConnection) { state._peerConnection.close(); state._peerConnection = null; } if (state._receiver) { state._receiver.cancel(); state._receiver = null; } state._dataChannel = null; }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
            return { startDownload: startDownload, cancelDownload: cancelDownload };
        })();
    })();
