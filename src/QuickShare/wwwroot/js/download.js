window.downloadApp = (function () {
            'use strict';
            var state = { phase: 'checking', downloading: false, isEncrypted: false, zkOpen: false, otpVerifying: false, verifiedOtp: '', adCountdown: 5, adTimer: null, downloadCount: 0, maxDownloads: 0, expiresLabel: '', previewStarted: false, fullText: null, bytes: null };

            // Text snippets are shown inline instead of forcing a download (issue #13).
            var TEXT_MIME_PREFIX = 'text/plain';
            var PREVIEW_MAX_BYTES = 1024 * 1024;
            var INLINE_FULL_BYTES = 10 * 1024;
            var PREVIEW_LINE_LIMIT = 50;

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
                    if (state.isEncrypted) show('dl-badge-encrypted'); else hide('dl-badge-encrypted');
                    if (state.zkOpen) show('dl-zk-panel'); else hide('dl-zk-panel');
                    text('dl-download-count', String(state.downloadCount)); text('dl-max-downloads', String(state.maxDownloads)); text('dl-expires-label', state.expiresLabel);
                    if (state.downloading) { attr('dl-download-btn', 'disabled', 'disabled'); text('dl-download-text', 'Decrypting\u2026'); document.getElementById('dl-download-icon').outerHTML = '<span class="db-spinner db-spinner--sm"></span>'; }
                    else { removeAttr('dl-download-btn', 'disabled'); text('dl-download-text', 'Download'); }
                    maybeRenderTextPreview();
                }
            }

            function infoFromDom() {
                var el = document.getElementById('download-info-data');
                try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
            }

            function init() {
                var infoEl = document.getElementById('download-info-data');
                if (!infoEl) { setTimeout(init, 50); return; }
                var hash = window.location.hash; if (hash && hash.startsWith('#key=')) state.isEncrypted = true;
                var info = infoFromDom();
                state.downloadCount = info.downloadCount || 0; state.maxDownloads = info.maxDownloads || 0; state.expiresLabel = info.expiresAt ? expiryLabel(info.expiresAt) : 'no expiry';
                if (info.hasOtp) state.phase = 'otp';
                else if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); }
                else state.phase = 'file';
                render();
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
                        var info = infoFromDom();
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

            function toggleZK() { state.zkOpen = !state.zkOpen; render(); }

            function downloadUrl() {
                var url = apiBase() + '/api/downloads/' + shareCode();
                if (state.verifiedOtp) url += '?otp=' + encodeURIComponent(state.verifiedOtp);
                return url;
            }

            function handleDownload() {
                // The preview already fetched (and paid for) the bytes — save those instead of
                // spending a second download against the delivery's limit.
                if (state.bytes) { saveBytes(state.bytes); return; }
                state.downloading = true; render();
                var url = downloadUrl();
                if (state.isEncrypted) { fetchBytes().then(saveBytes)['catch'](function (e) { if (window.DAUB) DAUB.toast('Download failed: ' + e.message, 'error'); })['finally'](function () { state.downloading = false; render(); }); }
                else { state.downloading = false; window.location.href = url; }
            }

            function saveBytes(bytes) { var blob = new Blob([bytes]); var dlUrl = URL.createObjectURL(blob); var a = document.createElement('a'); a.href = dlUrl; a.download = infoFromDom().fileName || shareCode(); document.body.appendChild(a); a.click(); document.body.removeChild(a); URL.revokeObjectURL(dlUrl); }

            async function fetchBytes() { var response = await fetch(downloadUrl()); if (!response.ok) throw new Error('Download failed: ' + response.statusText); var data = await response.arrayBuffer(); if (!state.isEncrypted) return data; var hash = window.location.hash; var keyMatch = hash.match(/key=([A-Za-z0-9\-_]+)/); if (!keyMatch) throw new Error('Encryption key not found'); var base64 = keyMatch[1].replace(/-/g, '+').replace(/_/g, '/'); while (base64.length % 4) base64 += '='; var binary = atob(base64); var keyBytes = new Uint8Array(binary.length); for (var i = 0; i < binary.length; i++) keyBytes[i] = binary.charCodeAt(i); var key = await crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['decrypt']); var nonce = data.slice(0, 12); var ciphertext = data.slice(12); return await crypto.subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(nonce) }, key, ciphertext); }

            function maybeRenderTextPreview() {
                if (state.previewStarted) return;
                var i = infoFromDom();
                if (!i.contentType || i.contentType.indexOf(TEXT_MIME_PREFIX) !== 0) return;
                if (!i.fileSize || i.fileSize > PREVIEW_MAX_BYTES) return;

                state.previewStarted = true;
                show('dl-text-preview');
                text('dl-text-status', 'Loading preview…');

                fetchBytes().then(function (bytes) {
                    state.bytes = bytes;
                    state.fullText = new TextDecoder().decode(bytes);
                    renderText(i.fileSize > INLINE_FULL_BYTES);
                })['catch'](function () {
                    // Preview is a nicety — fall back to the plain download button.
                    hide('dl-text-preview');
                });
            }

            function renderText(truncate) {
                var body = document.getElementById('dl-text-body');
                if (!body) return;
                var lines = state.fullText.split('\n');
                var truncated = truncate && lines.length > PREVIEW_LINE_LIMIT;
                // textContent, never innerHTML — the snippet is untrusted user content.
                body.textContent = truncated ? lines.slice(0, PREVIEW_LINE_LIMIT).join('\n') : state.fullText;
                if (truncated) { show('dl-text-more'); text('dl-text-status', 'First ' + PREVIEW_LINE_LIMIT + ' of ' + lines.length + ' lines'); }
                else { hide('dl-text-more'); text('dl-text-status', ''); }
            }

            function showFullText() { renderText(false); }

            function copyText() {
                if (!state.fullText || !navigator.clipboard) return;
                navigator.clipboard.writeText(state.fullText).then(function () {
                    text('dl-text-copy-label', 'Copied');
                    setTimeout(function () { text('dl-text-copy-label', 'Copy'); }, 2000);
                });
            }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
            return { verifyOtp: verifyOtp, otpInput: otpInput, otpKeydown: otpKeydown, skipAd: skipAd, toggleZK: toggleZK, handleDownload: handleDownload, showFullText: showFullText, copyText: copyText };
        })();