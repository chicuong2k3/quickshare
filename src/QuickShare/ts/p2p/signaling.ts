// P2P WebSocket signaling client for QuickShare.
// Establishes and manages WebSocket connection to the P2P signaling server.
(function () {
    'use strict';

    var MAX_RECONNECT_ATTEMPTS = 3;
    var RECONNECT_BACKOFF_MS = 1000;
    var WS_PROTOCOL = {
        http: 'ws:',
        https: 'wss:'
    };

    function resolveWebSocketBaseUrl(apiBaseUrl: string): string {
        if (!apiBaseUrl) {
            var protocol = window.location.protocol === 'https:' ? WS_PROTOCOL.https : WS_PROTOCOL.http;
            return protocol + '//' + window.location.host;
        }

        if (/^wss?:/i.test(apiBaseUrl)) {
            return apiBaseUrl;
        }

        return apiBaseUrl.replace(/^http:/, WS_PROTOCOL.http).replace(/^https:/, WS_PROTOCOL.https);
    }

    class SignalingClient implements P2PSignalingClient {
        apiBaseUrl: string;
        shareCode: string;
        ws: WebSocket | null;
        role: P2PRole | null;
        reconnectAttempts: number;
        _intentionalClose: boolean;

        // Event callbacks
        onPeerJoined: ((receiverId: string) => void) | null;
        onFileMeta: ((meta: P2PReceivedMeta) => void) | null;
        onRelay: ((type: string, data: unknown, receiverId?: string) => void) | null;
        onPeerLeft: ((message: string, receiverId?: string) => void) | null;
        onError: ((message: string) => void) | null;
        onRegistered: ((role: string) => void) | null;
        onWaiting: ((message: string) => void) | null;

        constructor(apiBaseUrl: string, shareCode: string) {
            this.apiBaseUrl = (apiBaseUrl || '').replace(/\/+$/, '');
            this.shareCode = shareCode;
            this.ws = null;
            this.role = null;
            this.reconnectAttempts = 0;
            this._intentionalClose = false;

            // Event callbacks
            this.onPeerJoined = null;
            this.onFileMeta = null;
            this.onRelay = null;
            this.onPeerLeft = null;
            this.onError = null;
            this.onRegistered = null;
            this.onWaiting = null;
        }

        connect(role: P2PRole, fileMeta?: P2PFileMeta): void {
            this.role = role;
            this._intentionalClose = false;

            var wsBaseUrl = resolveWebSocketBaseUrl(this.apiBaseUrl);
            var url = wsBaseUrl + '/ws/connect/' + encodeURIComponent(this.shareCode);
            var self = this;

            try {
                this.ws = new WebSocket(url);
            } catch (e) {
                if (self.onError) self.onError('WebSocket creation failed: ' + (e as Error).message);
                return;
            }

            this.ws.onopen = function () {
                self.reconnectAttempts = 0;
                var msg: { type: string; role: P2PRole; fileMeta?: P2PFileMeta } = { type: 'register', role: role };
                if (role === 'sender' && fileMeta) {
                    msg.fileMeta = {
                        name: fileMeta.name,
                        size: fileMeta.size,
                        mime: fileMeta.mime,
                        hasPin: fileMeta.hasPin || false,
                        pinHash: fileMeta.pinHash || null
                    };
                }
                self._sendMessage(msg);
            };

            this.ws.onmessage = function (event) {
                try {
                    var data = JSON.parse(event.data);
                    self._handleMessage(data);
                } catch (e) {
                    if (self.onError) self.onError('Invalid message: ' + (e as Error).message);
                }
            };

            this.ws.onclose = function () {
                if (!self._intentionalClose && self.reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
                    self.reconnectAttempts++;
                    var delay = RECONNECT_BACKOFF_MS * Math.pow(2, self.reconnectAttempts - 1);
                    setTimeout(function () { self.connect(role, fileMeta); }, delay);
                } else if (!self._intentionalClose && self.onError) {
                    self.onError('Connection lost');
                }
            };

            this.ws.onerror = function () {
                // onclose will fire after this
            };
        }

        send(type: string, payload?: unknown): void {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
                if (this.onError) this.onError('WebSocket not connected');
                return;
            }
            this._sendMessage({ type: type, data: payload });
        }

        sendTo(type: string, payload: unknown, receiverId: string): void {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
                if (this.onError) this.onError('WebSocket not connected');
                return;
            }
            this._sendMessage({ type: type, data: payload, receiverId: receiverId });
        }

        close(): void {
            this._intentionalClose = true;
            if (this.ws) {
                this.ws.close(1000, 'Client closing');
                this.ws = null;
            }
        }

        _sendMessage(msg: { type: string; data?: unknown; receiverId?: string }): void {
            if (this.ws && this.ws.readyState === WebSocket.OPEN) {
                this.ws.send(JSON.stringify(msg));
            }
        }

        _handleMessage(data: any): void {
            switch (data.type) {
                case 'registered':
                    if (this.onRegistered) this.onRegistered(data.role);
                    break;
                case 'peer-joined':
                    if (this.onPeerJoined) this.onPeerJoined(data.receiverId);
                    break;
                case 'file-meta':
                    if (this.onFileMeta) this.onFileMeta({
                        fileName: data.fileName,
                        fileSize: data.fileSize,
                        mimeType: data.mimeType,
                        hasPin: data.hasPin || false,
                        pinHash: data.pinHash || null
                    });
                    break;
                case 'waiting':
                    if (this.onWaiting) this.onWaiting(data.message);
                    break;
                case 'peer-left':
                    if (this.onPeerLeft) this.onPeerLeft(data.message, data.receiverId);
                    break;
                case 'error':
                    if (this.onError) this.onError(data.message || 'Server error');
                    break;
                case 'offer':
                case 'answer':
                case 'ice':
                case 'netprobe':
                    if (this.onRelay) this.onRelay(data.type, data.data, data.receiverId);
                    break;
            }
        }
    }

    window.P2P = window.P2P || ({} as P2PNamespace);
    window.P2P.SignalingClient = SignalingClient;
})();