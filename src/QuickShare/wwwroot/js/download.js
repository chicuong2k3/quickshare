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
                    if (state.isEncrypted) show('dl-fact-key'); else hide('dl-fact-key');
                    if (state.downloading) {
                        attr('dl-download-btn', 'aria-disabled', 'true');
                        text('dl-download-text', t('decrypting'));
                        // Hide the icon rather than destroy it: outerHTML removed the
                        // node permanently and the next render threw on null.
                        var icon = document.getElementById('dl-download-icon');
                        if (icon) icon.style.visibility = 'hidden';
                    } else {
                        removeAttr('dl-download-btn', 'aria-disabled');
                        text('dl-download-text', t('download'));
                        var icon2 = document.getElementById('dl-download-icon');
                        if (icon2) icon2.style.visibility = '';
                    }
                    maybeRenderTextPreview();
                }
            }

            // Copy comes from the server so this file carries no English literals.
            var T = (function () {
                var el = document.getElementById('download-i18n');
                try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
            })();

            function t(key, a, b) {
                var v = T[key] || '';
                return v.replace('{0}', a).replace('{1}', b);
            }

            function infoFromDom() {
                var el = document.getElementById('download-info-data');
                try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
            }

            var initAttempts = 0;

            function init() {
                var infoEl = document.getElementById('download-info-data');
                // Bounded: the old loop rescheduled itself forever on a page that
                // would never have the element.
                if (!infoEl) { if (++initAttempts < 40) setTimeout(init, 50); return; }
                var hash = window.location.hash; if (hash && hash.startsWith('#key=')) state.isEncrypted = true;
                var info = infoFromDom();
                state.downloadCount = info.downloadCount || 0; state.maxDownloads = info.maxDownloads || 0; state.expiresLabel = info.expiresAt ? expiryLabel(info.expiresAt) : t('expiryNone');
                if (info.hasOtp) state.phase = 'otp';
                else if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); }
                else state.phase = 'file';
                render();
            }
            function expiryLabel(e) {
                var r = new Date(e) - new Date();
                if (r <= 0) return t('expiryExpired');
                var m = Math.floor(r / 60000);
                if (m < 60) return t('expiryMin', m);
                var h = Math.floor(m / 60);
                if (h < 24) return t('expiryHour', h);
                return t('expiryDay', Math.floor(h / 24));
            }


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
                if (otp.length < 6) { show('dl-otp-error'); text('dl-otp-error', t('otpIncomplete')); return; }
                hide('dl-otp-error'); text('dl-otp-btn-text', t('otpVerifying')); attr('dl-otp-btn', 'disabled', 'disabled');
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
                        show('dl-otp-error'); text('dl-otp-error', e.message || (e.errors && e.errors[0] && e.errors[0].message) || t('otpWrong'));
                    })
                    .finally(function () { text('dl-otp-btn-text', t('otpUnlock')); removeAttr('dl-otp-btn', 'disabled'); render(); });
            }

            function startAdTimer() { state.adCountdown = 5; text('dl-ad-countdown', t('adCountdown', 5)); render();
                state.adTimer = setInterval(function () { state.adCountdown--; text('dl-ad-countdown', t('adCountdown', state.adCountdown)); if (state.adCountdown <= 0) { clearInterval(state.adTimer); state.adTimer = null; state.phase = 'file'; render(); } }, 1000); }
            function skipAd() { if (state.adTimer) { clearInterval(state.adTimer); state.adTimer = null; } state.phase = 'file'; render(); }

            function toggleZK() { state.zkOpen = !state.zkOpen; render(); }

            function downloadUrl() {
                var url = apiBase() + '/api/downloads/' + shareCode();
                if (state.verifiedOtp) url += '?otp=' + encodeURIComponent(state.verifiedOtp);
                return url;
            }

            // Returns false to cancel the anchor's own navigation, true to let it
            // happen. With JS dead the anchor still delivers an unencrypted file.
            function handleDownload(event) {
                if (state.bytes) { saveBytes(state.bytes); return false; }
                if (!state.isEncrypted && !state.verifiedOtp) return true;
                if (event && event.preventDefault) event.preventDefault();
                state.downloading = true; render();
                if (state.isEncrypted) {
                    fetchBytes().then(saveBytes)['catch'](function (e) {
                        if (window.DAUB) DAUB.toast(t('downloadFailed') + ': ' + e.message, 'error');
                    })['finally'](function () { state.downloading = false; render(); });
                } else {
                    state.downloading = false;
                    window.location.href = downloadUrl();
                }
                return false;
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
                text('dl-text-status', t('previewLoading'));

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
                if (truncated) { show('dl-text-more'); text('dl-text-status', t('previewLines', PREVIEW_LINE_LIMIT, lines.length)); }
                else { hide('dl-text-more'); text('dl-text-status', ''); }
            }

            function showFullText() { renderText(false); }

            function copyText() {
                if (!state.fullText || !navigator.clipboard) return;
                navigator.clipboard.writeText(state.fullText).then(function () {
                    text('dl-text-copy-label', t('copied'));
                    setTimeout(function () { text('dl-text-copy-label', t('copy')); }, 2000);
                });
            }

            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
            return { verifyOtp: verifyOtp, otpInput: otpInput, otpKeydown: otpKeydown, skipAd: skipAd, toggleZK: toggleZK, handleDownload: handleDownload, showFullText: showFullText, copyText: copyText };
        })();