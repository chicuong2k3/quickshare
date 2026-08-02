// WebRTC peer connection wrapper for QuickShare P2P transfers.
// Handles RTCPeerConnection lifecycle, data channels, and ICE candidate exchange.
(function () {
    'use strict';

    // P2P direct only — no TURN. Relay costs like hosting the file, and the
    // server has a standard R2 send/receive path for that. When peers cannot
    // reach each other directly (different networks/NAT), the UI tells the
    // user instead of relaying through a bandwidth-hungry TURN server.
    var ICE_SERVERS = [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun2.l.google.com:19302' },
        { urls: 'stun:stun3.l.google.com:19302' },
        { urls: 'stun:stun4.l.google.com:19302' }
    ];

    function PeerConnection() {
        this.pc = null;
        this.pendingCandidates = [];
        this.remoteSet = false;

        // Event callbacks
        this.onIceCandidate = null;
        this.onDataChannel = null;
        this.onConnectionState = null;
        this.onError = null;
    }

    function resolveIceServers() {
        return ICE_SERVERS;
    }

    PeerConnection.prototype._ensurePC = function () {
        if (this.pc) return;
        var self = this;

        try {
            this.pc = new RTCPeerConnection({ iceServers: resolveIceServers() });
        } catch (e) {
            if (self.onError) self.onError('RTCPeerConnection creation failed: ' + e.message);
            return;
        }

        this.pc.onicecandidate = function (event) {
            if (event.candidate && self.onIceCandidate) {
                self.onIceCandidate(event.candidate.toJSON());
            }
        };

        this.pc.ondatachannel = function (event) {
            if (self.onDataChannel) self.onDataChannel(event.channel);
        };

        this.pc.onconnectionstatechange = function () {
            if (self.onConnectionState) self.onConnectionState(self.pc.connectionState);
        };
    };

    PeerConnection.prototype.createDataChannel = function (label) {
        this._ensurePC();
        if (!this.pc) return null;
        return this.pc.createDataChannel(label || 'filetransfer');
    };

    PeerConnection.prototype.createOffer = function () {
        var self = this;
        this._ensurePC();
        if (!this.pc) return Promise.reject(new Error('PC not initialized'));

        return this.pc.createOffer({ offerToReceiveAudio: false, offerToReceiveVideo: false })
            .then(function (offer) {
                return self.pc.setLocalDescription(offer);
            })
            .then(function () {
                return self.pc.localDescription.sdp;
            })
            .catch(function (e) {
                if (self.onError) self.onError('Create offer failed: ' + e.message);
                throw e;
            });
    };

    PeerConnection.prototype.createAnswer = function (offerSdp) {
        var self = this;
        this._ensurePC();
        if (!this.pc) return Promise.reject(new Error('PC not initialized'));

        return this.pc.setRemoteDescription({ type: 'offer', sdp: offerSdp })
            .then(function () {
                self.remoteSet = true;
                return self._flushPendingCandidates();
            })
            .then(function () {
                return self.pc.createAnswer();
            })
            .then(function (answer) {
                return self.pc.setLocalDescription(answer);
            })
            .then(function () {
                return self.pc.localDescription.sdp;
            })
            .catch(function (e) {
                if (self.onError) self.onError('Create answer failed: ' + e.message);
                throw e;
            });
    };

    PeerConnection.prototype.setRemoteSdp = function (sdp) {
        var self = this;
        this._ensurePC();
        if (!this.pc) return Promise.reject(new Error('PC not initialized'));

        return this.pc.setRemoteDescription({ type: 'answer', sdp: sdp })
            .then(function () {
                self.remoteSet = true;
                return self._flushPendingCandidates();
            })
            .catch(function (e) {
                if (self.onError) self.onError('Set remote SDP failed: ' + e.message);
                throw e;
            });
    };

    PeerConnection.prototype.addIceCandidate = function (candidate) {
        if (this.remoteSet && this.pc) {
            try {
                this.pc.addIceCandidate(new RTCIceCandidate(candidate));
            } catch (e) {
                if (this.onError) this.onError('Add ICE candidate failed: ' + e.message);
            }
        } else {
            this.pendingCandidates.push(candidate);
        }
    };

    PeerConnection.prototype._flushPendingCandidates = function () {
        var self = this;
        if (!this.pc) return Promise.resolve();
        var promises = this.pendingCandidates.map(function (c) {
            try {
                return self.pc.addIceCandidate(new RTCIceCandidate(c));
            } catch (e) {
                return Promise.resolve();
            }
        });
        this.pendingCandidates = [];
        return Promise.all(promises);
    };

    PeerConnection.prototype.close = function () {
        if (this.pc) {
            this.pc.close();
            this.pc = null;
        }
    };

    window.P2P = window.P2P || {};
    window.P2P.PeerConnection = PeerConnection;
})();
