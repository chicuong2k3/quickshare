(function () {
        'use strict';
        function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
        function formatBytes(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }
        function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
        function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }
        function text(id, val) { var el = document.getElementById(id); if (el) el.textContent = val; }

        window.p2pDownload = (function () {
            var state = {
                phase: 'checking', progress: 0, fileName: '', fileSize: 0, mimeType: '',
                shareCode: (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.shareCode || ''), error: '', fallbackUrl: '/d/' + (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.shareCode || ''),
                _receivedBytes: 0, _startTime: 0,
                _signaling: null, _peerConnection: null, _dataChannel: null, _receiver: null, _intentionalStop: false,
                _pendingOffer: null, _pendingIceCandidates: []
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
                    if (state.phase === 'connecting') { if (btn) btn.disabled = true; if (btnTxt) btnTxt.textContent = 'Connecting\u2026'; }
                    else { if (btn) btn.disabled = false; if (btnTxt) btnTxt.textContent = 'Download directly'; }
                }
                if (state.phase === 'transferring') {
                    text('p2pd-transfer-name', state.fileName);
                    text('p2pd-transfer-progress', progressLabel());
                    
            document.getElementById('p2pd-progress-bar').setAttribute('aria-valuenow', state.progress);
                }
                if (state.phase === 'complete') text('p2pd-complete-name', state.fileName);
                if (state.phase === 'error') {
                    text('p2pd-error-msg', state.error || 'Connection failed');
                    if (state.fallbackUrl) { show('p2pd-fallback'); document.getElementById('p2pd-fallback').href = state.fallbackUrl; }
                }
            }

            function progressLabel() {
                if (state.progress === 0) return 'Starting...';
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

            function init() {
                render();
                var api = apiBase();
                if (!window.P2P || !window.P2P.SignalingClient) { state.phase = 'error'; state.error = 'Signaling client not loaded'; render(); return; }
                state._signaling = new window.P2P.SignalingClient(api, state.shareCode);
                state._signaling.onRegistered = function () {};
                state._signaling.onFileMeta = function (meta) { state.fileName = meta.fileName; state.fileSize = meta.fileSize; state.mimeType = meta.mimeType; if (state.phase !== 'ready' && state.phase !== 'connecting' && state.phase !== 'transferring' && state.phase !== 'complete') { state.phase = 'ready'; } render(); };
                state._signaling.onPeerJoined = function () { if (state.phase === 'waiting-sender' || state.phase === 'checking') { state.phase = 'ready'; render(); } };
                state._signaling.onWaiting = function () { state.phase = 'waiting-sender'; render(); };
                state._signaling.onRelay = function (type, data) {
                    if (type === 'offer') {
                        if (state._peerConnection) {
                            handleOffer(data);
                        } else {
                            state._pendingOffer = data;
                        }
                    } else if (type === 'ice' && data) {
                        var candidate = data.candidate || data;
                        if (state._peerConnection) {
                            state._peerConnection.addIceCandidate(candidate);
                        } else {
                            state._pendingIceCandidates.push(candidate);
                        }
                    }
                };
                state._signaling.connect('receiver');
            }

            function startDownload() {
                state.phase = 'connecting'; render();
                if (!window.P2P.PeerConnection || !window.P2P.FileReceiver) { state.phase = 'error'; state.error = 'P2P modules not loaded'; render(); return; }
                state._peerConnection = new window.P2P.PeerConnection();
                state._peerConnection.onDataChannel = function (channel) { onDataChannel(channel); };
                state._peerConnection.onIceCandidate = function (c) { if (state._signaling) state._signaling.send('ice', c); };
                state._peerConnection.onConnectionState = function (s) { if ((s === 'failed' || s === 'disconnected') && (state.phase === 'transferring' || state.phase === 'connecting')) { state.phase = 'error'; state.error = 'Connection ' + s; render(); } };
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
                state._peerConnection.createAnswer(sdp).then(function (answerSdp) { state._signaling.send('answer', { sdp: answerSdp }); }).catch(function () { state.phase = 'error'; state.error = 'Failed to handle WebRTC offer'; render(); });
            }

            function onDataChannel(channel) {
                state._dataChannel = channel;
                state._receiver = new window.P2P.FileReceiver(channel, null);
                state._startTime = Date.now();
                state._receiver.onFileInfo = function (info) { state.fileName = info.name; state.fileSize = info.size; state.mimeType = info.mime; };
                state._receiver.onProgress = function (rec, tot) { state.progress = Math.round((rec / tot) * 100); state._receivedBytes = rec; render(); };
                state._receiver.onComplete = function () { state.progress = 100; state.phase = 'complete'; render(); };
                state._receiver.onError = function (msg) { state.phase = 'error'; state.error = msg; render(); };
                state.phase = 'transferring'; state._receiver.start(); render();
            }

            function cancelDownload() { state._intentionalStop = true; if (state._receiver) state._receiver.cancel(); cleanup(); state.phase = 'checking'; state._intentionalStop = false; render(); }
            function cleanup() { if (state._signaling) { state._signaling.close(); state._signaling = null; } if (state._peerConnection) { state._peerConnection.close(); state._peerConnection = null; } if (state._receiver) { state._receiver.cancel(); state._receiver = null; } state._dataChannel = null; }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
            return { startDownload: startDownload, cancelDownload: cancelDownload };
        })();
    })();
