window.downloadApp = (function () {
            'use strict';
            var state = { phase: 'checking', downloading: false, isEncrypted: false, zkOpen: false, otpVerifying: false, verifiedOtp: '', adCountdown: 5, adTimer: null, brandLogo: '', brandMessage: '', brandAccent: '', downloadCount: 0, maxDownloads: 0, expiresLabel: '' };

            function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
            function shareCode() { var m = window.location.pathname.match(/\/d\/(.+)/); return m ? m[1] : ''; }
            function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
            function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }
            function text(id, val) { var el = document.getElementById(id); if (el) el.textContent = val; }
            function attr(id, name, val) { var el = document.getElementById(id); if (el) el.setAttribute(name, val); }
            function removeAttr(id, name) { var el = document.getElementById(id); if (el) el.removeAttribute(name); }

            function render() {
                hide('download-phase-otp'); hide('download-phase-ad'); hide('download-phase-file');
                if (state.phase === 'otp') show('download-phase-otp');
                else if (state.phase === 'ad') show('download-phase-ad');
                else if (state.phase === 'file') {
                    show('download-phase-file');
                    if (state.brandLogo || state.brandMessage) { show('dl-branding'); if (state.brandLogo) { show('dl-brand-logo'); attr('dl-brand-logo', 'src', state.brandLogo); } else hide('dl-brand-logo'); text('dl-brand-message', state.brandMessage || 'Sent a file for you'); }
                    else hide('dl-branding');
                    if (state.isEncrypted) show('dl-badge-encrypted'); else hide('dl-badge-encrypted');
                    if (state.zkOpen) show('dl-zk-panel'); else hide('dl-zk-panel');
                    text('dl-download-count', String(state.downloadCount)); text('dl-max-downloads', String(state.maxDownloads)); text('dl-expires-label', state.expiresLabel);
                    if (state.downloading) { attr('dl-download-btn', 'disabled', 'disabled'); text('dl-download-text', 'Decrypting\u2026'); document.getElementById('dl-download-icon').outerHTML = '<span class="db-spinner db-spinner--sm"></span>'; }
                    else { removeAttr('dl-download-btn', 'disabled'); text('dl-download-text', 'Download'); }
                }
            }

            function init() {
                var infoEl = document.getElementById('download-info-data');
                if (!infoEl) { setTimeout(init, 50); return; }
                var hash = window.location.hash; if (hash && hash.startsWith('#key=')) state.isEncrypted = true;
                var info = {}; try { info = JSON.parse(infoEl.textContent || '{}'); } catch (_) {}
                state.downloadCount = info.downloadCount || 0; state.maxDownloads = info.maxDownloads || 0; state.expiresLabel = info.expiresAt ? expiryLabel(info.expiresAt) : 'no expiry';
                if (info.hasOtp) state.phase = 'otp';
                else if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); }
                else state.phase = 'file';
                render(); fetchBranding();
            }
            function expiryLabel(e) { var r = new Date(e) - new Date(); if (r <= 0) return 'expired'; var m = Math.floor(r / 60000); if (m < 60) return 'expires in ' + m + ' min'; var h = Math.floor(m / 60); if (h < 24) return 'expires in ' + h + ' h'; var d = Math.floor(h / 24); return 'expires in ' + d + ' day' + (d === 1 ? '' : 's'); }


            function readOtp() {
                var inputs = document.querySelectorAll('#dl-otp .db-otp__input');
                var pin = '';
                for (var i = 0; i < inputs.length; i++) pin += inputs[i].value;
                return pin;
            }

            function otpInput(event, idx) {
                var val = event.target.value.replace(/\D/g, '');
                event.target.value = val;
                if (val && idx < 5) {
                    var next = document.querySelectorAll('#dl-otp .db-otp__input')[idx + 1];
                    if (next) next.focus();
                }
            }

            function otpKeydown(event, idx) {
                if (event.key === 'Backspace' && !event.target.value && idx > 0) {
                    var prev = document.querySelectorAll('#dl-otp .db-otp__input')[idx - 1];
                    if (prev) prev.focus();
                }
                if (event.key === 'Enter') verifyOtp();
            }

            function verifyOtp() {
                var otp = readOtp();
                if (otp.length < 6) { show('dl-otp-error'); text('dl-otp-error', 'Please enter all 6 digits.'); return; }
                hide('dl-otp-error'); text('dl-otp-btn-text', 'Verifying\u2026'); attr('dl-otp-btn', 'disabled', 'disabled');
                fetch(apiBase() + '/api/deliveries/' + shareCode() + '/verify-otp', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ otp: otp }) })
                    .then(function (r) {
                        return r.ok ? r.json().catch(function () { return {}; }) : r.json().then(function (b) { throw b; }).catch(function () { throw {}; });
                    })
                    .then(function () {
                        state.verifiedOtp = otp;
                        var infoEl = document.getElementById('download-info-data'); var info = {}; try { info = JSON.parse(infoEl.textContent || '{}'); } catch (_) {}
                        if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); }
                        else state.phase = 'file';
                    })
                    .catch(function (e) {
                        show('dl-otp-error'); text('dl-otp-error', e.message || (e.errors && e.errors[0] && e.errors[0].message) || 'Incorrect code.');
                    })
                    .finally(function () { text('dl-otp-btn-text', 'Unlock'); removeAttr('dl-otp-btn', 'disabled'); render(); });
            }

            function startAdTimer() { state.adCountdown = 5; text('dl-ad-countdown', 'Continue in 5s'); render();
                state.adTimer = setInterval(function () { state.adCountdown--; text('dl-ad-countdown', 'Continue in ' + state.adCountdown + 's'); if (state.adCountdown <= 0) { clearInterval(state.adTimer); state.adTimer = null; state.phase = 'file'; render(); } }, 1000); }
            function skipAd() { if (state.adTimer) { clearInterval(state.adTimer); state.adTimer = null; } state.phase = 'file'; render(); }

            function fetchBranding() { fetch(apiBase() + '/api/deliveries/' + shareCode() + '/brand').then(function (r) { if (r.ok) return r.json(); }).then(function (data) { if (data) { if (data.logo) state.brandLogo = data.logo; if (data.message) state.brandMessage = data.message; if (data.accent) state.brandAccent = data.accent; render(); } }).catch(function () {}); }
            function toggleZK() { state.zkOpen = !state.zkOpen; render(); }
            function handleDownload() { state.downloading = true; render(); var code = shareCode(); var url = apiBase() + '/api/downloads/' + code;
                if (state.verifiedOtp) url += '?otp=' + encodeURIComponent(state.verifiedOtp);
                if (state.isEncrypted) { decryptAndSave(url)['catch'](function (e) { if (window.DAUB) DAUB.toast('Download failed: ' + e.message, 'error'); })['finally'](function () { state.downloading = false; render(); }); }
                else { state.downloading = false; window.location.href = url; } }

            async function decryptAndSave(url) { var hash = window.location.hash; var keyMatch = hash.match(/key=([A-Za-z0-9\-_]+)/); if (!keyMatch) throw new Error('Encryption key not found'); var keyB64 = keyMatch[1]; var base64 = keyB64.replace(/-/g, '+').replace(/_/g, '/'); while (base64.length % 4) base64 += '='; var binary = atob(base64); var keyBytes = new Uint8Array(binary.length); for (var i = 0; i < binary.length; i++) keyBytes[i] = binary.charCodeAt(i); var key = await crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['decrypt']); var response = await fetch(url); if (!response.ok) throw new Error('Download failed: ' + response.statusText); var encryptedData = await response.arrayBuffer(); var nonce = encryptedData.slice(0, 12); var ciphertext = encryptedData.slice(12); var decrypted = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(nonce) }, key, ciphertext); var blob = new Blob([decrypted]); var dlUrl = URL.createObjectURL(blob); var a = document.createElement('a'); a.href = dlUrl; a.download = code; document.body.appendChild(a); a.click(); document.body.removeChild(a); URL.revokeObjectURL(dlUrl); }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
            return { verifyOtp: verifyOtp, otpInput: otpInput, otpKeydown: otpKeydown, skipAd: skipAd, toggleZK: toggleZK, handleDownload: handleDownload };
        })();