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

    // Network preflight. A server-reflexive (srflx) candidate carries the public
    // IPv4 address a peer is seen as from the internet. Two devices behind the
    // same NAT report the same one, so a mismatch means a direct connection is
    // impossible and the UI can say so up front instead of waiting out an ICE
    // timeout. Inconclusive (no srflx candidate) is never treated as a failure.
    var PROBE_TIMEOUT_MS = 5000;
    var IPV4_PATTERN = /^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$/;
    var CANDIDATE_TYPE_FIELD = 'typ';
    var CANDIDATE_TYPE_SRFLX = 'srflx';
    var CANDIDATE_ADDRESS_INDEX = 4;
    var PROBE_CHANNEL_LABEL = 'netprobe';
    var NETWORK_RESULT = {
        same: 'same',
        different: 'different',
        unknown: 'unknown'
    };

    function extractPublicIpv4(candidateLine) {
        var parts = (candidateLine || '').split(' ');
        var typeIndex = parts.indexOf(CANDIDATE_TYPE_FIELD);
        if (typeIndex < 0 || parts[typeIndex + 1] !== CANDIDATE_TYPE_SRFLX) {
            return null;
        }

        var address = parts[CANDIDATE_ADDRESS_INDEX];
        // IPv6 is skipped on purpose: two devices on one LAN still get distinct
        // global IPv6 addresses, so it cannot separate same from different network.
        if (!address || !IPV4_PATTERN.test(address)) {
            return null;
        }

        return address;
    }

    // Resolves to the public IPv4 string, or null when it cannot be determined
    // (STUN blocked, IPv6-only, timeout). Never rejects.
    function probePublicAddress(timeoutMs) {
        return new Promise(function (resolve) {
            var pc = null;
            var settled = false;
            var timer = null;

            function finish(address) {
                if (settled) {
                    return;
                }
                settled = true;
                if (timer) {
                    clearTimeout(timer);
                }
                if (pc) {
                    try {
                        pc.close();
                    } catch (e) {
                        // Already closed.
                    }
                }
                resolve(address || null);
            }

            try {
                pc = new RTCPeerConnection({ iceServers: resolveIceServers() });
            } catch (e) {
                finish(null);
                return;
            }

            pc.onicecandidate = function (event) {
                if (!event.candidate) {
                    finish(null); // Gathering finished with no usable srflx candidate.
                    return;
                }
                var address = extractPublicIpv4(event.candidate.candidate);
                if (address) {
                    finish(address);
                }
            };

            timer = setTimeout(function () { finish(null); }, timeoutMs || PROBE_TIMEOUT_MS);

            try {
                pc.createDataChannel(PROBE_CHANNEL_LABEL);
                pc.createOffer()
                    .then(function (offer) { return pc.setLocalDescription(offer); })
                    .catch(function () { finish(null); });
            } catch (e) {
                finish(null);
            }
        });
    }

    function compareNetwork(localAddress, remoteAddress) {
        if (!localAddress || !remoteAddress) {
            return NETWORK_RESULT.unknown;
        }

        return localAddress === remoteAddress ? NETWORK_RESULT.same : NETWORK_RESULT.different;
    }

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
    window.P2P.probePublicAddress = probePublicAddress;
    window.P2P.compareNetwork = compareNetwork;
    window.P2P.NETWORK_RESULT = NETWORK_RESULT;
})();
