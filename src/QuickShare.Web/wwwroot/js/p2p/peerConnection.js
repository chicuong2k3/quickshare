// WebRTC peer connection wrapper for QuickShare P2P transfers.
// Handles RTCPeerConnection lifecycle, data channels, and ICE candidate exchange.
(function () {
    'use strict';

    var ICE_SERVERS = [{ urls: 'stun:stun.l.google.com:19302' }];

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

    PeerConnection.prototype._ensurePC = function () {
        if (this.pc) return;
        var self = this;

        try {
            this.pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
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
