window.downloadApp = (function (): DownloadPage {
    'use strict';
    const state: {
        phase: string;
        downloading: boolean;
        isEncrypted: boolean;
        zkOpen: boolean;
        otpVerifying: boolean;
        verifiedOtp: string;
        verifiedPassword: string;
        adCountdown: number;
        adTimer: ReturnType<typeof setInterval> | null;
        expiresAt: Date | null;
        expiryTimer: ReturnType<typeof setInterval> | null;
        expiryLevel: string;
        scheduledAt: Date | null;
        scheduledTimer: ReturnType<typeof setInterval> | null;
        downloadCount: number;
        maxDownloads: number;
        expiresLabel: string;
        previewStarted: boolean;
        mediaPreviewStarted: boolean;
        fullText: string | null;
        bytes: ArrayBuffer | null;
    } = { phase: 'checking', downloading: false, isEncrypted: false, zkOpen: false, otpVerifying: false, verifiedOtp: '', verifiedPassword: '', adCountdown: 5, adTimer: null, expiresAt: null, expiryTimer: null, expiryLevel: '', scheduledAt: null, scheduledTimer: null, downloadCount: 0, maxDownloads: 0, expiresLabel: '', previewStarted: false, mediaPreviewStarted: false, fullText: null, bytes: null };

    // Text snippets are shown inline instead of forcing a download (issue #13).
    const TEXT_MIME_PREFIX = 'text/plain';
    const PREVIEW_MAX_BYTES = 1024 * 1024;
    const INLINE_FULL_BYTES = 10 * 1024;
    const PREVIEW_LINE_LIMIT = 50;

    function apiBase(): string { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
    function shareCode(): string { const m = window.location.pathname.match(/\/d\/(.+)/); return m ? m[1] : ''; }

    // Anonymous ad-interstitial analytics beacon (issue #24). Fire-and-forget;
    // a failed beacon must never block the download flow.
    function trackAdView(): void {
        try {
            navigator.sendBeacon('/api/analytics/track',
                new Blob([JSON.stringify({ event: 'ad_interstitial_viewed' })], { type: 'application/json' }));
        } catch (_) { /* ignore */ }
    }
    function show(id: string) { const el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id: string) { const el = document.getElementById(id); if (el) el.classList.add('hidden'); }
    function text(id: string, val: string) { const el = document.getElementById(id); if (el) el.textContent = val; }
    function attr(id: string, name: string, val: string) { const el = document.getElementById(id); if (el) el.setAttribute(name, val); }
    function removeAttr(id: string, name: string) { const el = document.getElementById(id); if (el) el.removeAttribute(name); }

    function render() {
        hide('download-phase-password'); hide('download-phase-otp'); hide('download-phase-ad'); hide('download-phase-file'); hide('download-phase-expired');
        if (state.phase === 'password') show('download-phase-password');
        else if (state.phase === 'otp') show('download-phase-otp');
        else if (state.phase === 'ad') show('download-phase-ad');
        else if (state.phase === 'expired') show('download-phase-expired');
        else if (state.phase === 'file') {
            show('download-phase-file');
            if (state.isEncrypted) show('dl-badge-encrypted'); else hide('dl-badge-encrypted');
            if (state.zkOpen) show('dl-zk-panel'); else hide('dl-zk-panel');
            text('dl-download-count', String(state.downloadCount)); text('dl-max-downloads', String(state.maxDownloads));
            if (state.expiresAt) tickExpiry(); else { text('dl-spec-expires', state.expiresLabel); text('dl-expires-label', state.expiresLabel); }
            if (state.isEncrypted) show('dl-fact-key'); else hide('dl-fact-key');
            if (state.downloading) {
                attr('dl-download-btn', 'aria-disabled', 'true');
                text('dl-download-text', t('decrypting'));
                // Hide the icon rather than destroy it: outerHTML removed the
                // node permanently and the next render threw on null.
                const icon = document.getElementById('dl-download-icon');
                if (icon) icon.style.visibility = 'hidden';
            } else {
                removeAttr('dl-download-btn', 'aria-disabled');
                text('dl-download-text', t('download'));
                const icon2 = document.getElementById('dl-download-icon');
                if (icon2) icon2.style.visibility = '';
            }
            maybeRenderTextPreview();
            maybeRenderMediaPreview();
        }
    }

    // Copy comes from the server so this file carries no English literals.
    const T: Record<string, string> = (function () {
        const el = document.getElementById('download-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    })();

    function t(key: string, a?: string | number, b?: string | number): string {
        const v = T[key] || '';
        return v.replace('{0}', String(a)).replace('{1}', String(b));
    }

    function infoFromDom(): any {
        const el = document.getElementById('download-info-data');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    }

    let initAttempts = 0;

    function init() {
        const infoEl = document.getElementById('download-info-data');
        // Bounded: the old loop rescheduled itself forever on a page that
        // would never have the element.
        if (!infoEl) { if (++initAttempts < 40) setTimeout(init, 50); return; }
        const hash = window.location.hash; if (hash && hash.startsWith('#key=')) state.isEncrypted = true;
        const info = infoFromDom();
        state.downloadCount = info.downloadCount || 0; state.maxDownloads = info.maxDownloads || 0; state.expiresAt = info.expiresAt ? new Date(info.expiresAt) : null; state.expiresLabel = state.expiresAt ? formatExpiry().label : t('expiryNone');
        state.scheduledAt = info.deliverAt ? new Date(info.deliverAt) : null;
        if (info.status === 'Scheduled' && state.scheduledAt && state.scheduledAt.getTime() > Date.now()) {
            startScheduledCountdown();
            return;
        }
        if (info.hasPassword) state.phase = 'password';
        else if (info.hasOtp) state.phase = 'otp';
        else if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); trackAdView(); }
        else state.phase = 'file';
        if (state.expiresAt) startExpiryTimer();
        render();
    }
    function pad2(n: number): string { return (n < 10 ? '0' : '') + n; }

    // Live expiry countdown (issue #113). Server renders a static label;
    // this only refines it — with JS dead the anchor still delivers.
    function formatExpiry(): { expired: boolean; label: string; level: string } {
        if (!state.expiresAt) return { expired: false, label: t('expiryNone'), level: 'subtle' };
        const r = state.expiresAt.getTime() - Date.now();
        if (r <= 0) return { expired: true, label: t('expiryExpired'), level: 'urgent' };
        const totalSec = Math.floor(r / 1000);
        const h = Math.floor(totalSec / 3600);
        const clock = h + ':' + pad2(Math.floor(totalSec / 60) % 60) + ':' + pad2(totalSec % 60);
        if (h < 1) return { expired: false, label: t('expiryCountdown', clock), level: 'urgent' };
        if (h < 24) return { expired: false, label: t('expiryCountdown', clock), level: 'warn' };
        return { expired: false, label: t('expiryDay', Math.floor(h / 24)), level: 'subtle' };
    }

    function applyExpiryClass(level: string) {
        const ids = ['dl-spec-expires', 'dl-expires-label'];
        for (let i = 0; i < ids.length; i++) {
            const el = document.getElementById(ids[i]);
            if (!el) continue;
            el.classList.remove('dl-expiry--subtle', 'dl-expiry--warn', 'dl-expiry--urgent');
            if (level) el.classList.add('dl-expiry--' + level);
        }
    }

    function setNudge(showNudge: boolean) {
        const el = document.getElementById('dl-expiry-nudge');
        if (!el) return;
        if (showNudge) el.classList.remove('hidden'); else el.classList.add('hidden');
    }

    function tickExpiry() {
        if (!state.expiresAt) return;
        const r = state.expiresAt.getTime() - Date.now();
        if (r <= 0) {
            if (state.expiryTimer) { clearInterval(state.expiryTimer); state.expiryTimer = null; }
            if (state.adTimer) { clearInterval(state.adTimer); state.adTimer = null; }
            state.expiryLevel = 'urgent';
            state.expiresLabel = t('expiryExpired');
            state.phase = 'expired';
            render();
            return;
        }
        const f = formatExpiry();
        state.expiryLevel = f.level;
        state.expiresLabel = f.label;
        if (state.phase !== 'file') return;
        text('dl-spec-expires', f.label);
        text('dl-expires-label', f.label);
        applyExpiryClass(f.level);
        setNudge(f.level === 'urgent');
        if (f.level === 'urgent') attr('dl-spec-expires', 'aria-live', 'polite'); else removeAttr('dl-spec-expires', 'aria-live');
    }

    function startExpiryTimer() {
        tickExpiry();
        state.expiryTimer = setInterval(tickExpiry, 1000);
    }

    // Live scheduled-delivery countdown (issue #69). The server renders the
    // "scheduled" panel; this ticks the countdown and reloads at activation
    // time so the download flow appears without a manual refresh.
    function startScheduledCountdown() {
        tickScheduled();
        state.scheduledTimer = setInterval(tickScheduled, 1000);
    }

    function tickScheduled() {
        if (!state.scheduledAt) return;
        const r = state.scheduledAt.getTime() - Date.now();
        if (r <= 0) {
            if (state.scheduledTimer) { clearInterval(state.scheduledTimer); state.scheduledTimer = null; }
            window.location.reload();
            return;
        }
        const totalSec = Math.floor(r / 1000);
        const d = Math.floor(totalSec / 86400);
        const h = Math.floor((totalSec % 86400) / 3600);
        const m = Math.floor((totalSec % 3600) / 60);
        const s = totalSec % 60;
        const label = d > 0 ? (d + 'd ' + h + 'h') : (h + ':' + pad2(m) + ':' + pad2(s));
        text('dl-scheduled-countdown', t('scheduledCountdown', label));
    }


    function readOtp(): string {
        const inputs = document.querySelectorAll<HTMLInputElement>('#dl-otp .db-otp__input');
        let pin = '';
        for (let i = 0; i < inputs.length; i++) pin += inputs[i].value;
        return pin;
    }

    function otpInput(event: Event, idx: number) {
        const target = event.target as HTMLInputElement;
        const val = target.value.replace(/\D/g, '');
        target.value = val;
        if (val && idx < 5) {
            const next = document.querySelectorAll<HTMLInputElement>('#dl-otp .db-otp__input')[idx + 1];
            if (next) next.focus();
        }
    }

    function otpKeydown(event: KeyboardEvent, idx: number) {
        const target = event.target as HTMLInputElement;
        if (event.key === 'Backspace' && !target.value && idx > 0) {
            const prev = document.querySelectorAll<HTMLInputElement>('#dl-otp .db-otp__input')[idx - 1];
            if (prev) prev.focus();
        }
        if (event.key === 'Enter') verifyOtp();
    }

    function verifyOtp() {
        const otp = readOtp();
        if (otp.length < 6) { show('dl-otp-error'); text('dl-otp-error', t('otpIncomplete')); return; }
        hide('dl-otp-error'); text('dl-otp-btn-text', t('otpVerifying')); attr('dl-otp-btn', 'disabled', 'disabled');
        fetch(apiBase() + '/api/deliveries/' + shareCode() + '/verify-otp', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ otp: otp }) })
            .then(function (r) {
                return r.ok ? r.json().catch(function () { return {}; }) : r.json().then(function (b) { throw b; }).catch(function () { throw {}; });
            })
            .then(function () {
                state.verifiedOtp = otp;
                const info = infoFromDom();
                if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); trackAdView(); }
                else state.phase = 'file';
            })
            .catch(function (e) {
                show('dl-otp-error'); text('dl-otp-error', e.message || (e.errors && e.errors[0] && e.errors[0].message) || t('otpWrong'));
            })
            .finally(function () { text('dl-otp-btn-text', t('otpUnlock')); removeAttr('dl-otp-btn', 'disabled'); render(); });
    }

    function readPassword(): string {
        const input = document.getElementById('dl-password') as HTMLInputElement | null;
        return input ? input.value : '';
    }

    function passwordKeydown(event: KeyboardEvent) {
        if (event.key === 'Enter') verifyPassword();
    }

    function verifyPassword() {
        const password = readPassword();
        if (!password) { show('dl-password-error'); text('dl-password-error', t('passwordIncomplete')); return; }
        hide('dl-password-error'); text('dl-password-btn-text', t('passwordVerifying')); attr('dl-password-btn', 'disabled', 'disabled');
        fetch(apiBase() + '/api/deliveries/' + shareCode() + '/verify-password', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password: password }) })
            .then(function (r) {
                return r.ok ? r.json().catch(function () { return {}; }) : r.json().then(function (b) { throw b; }).catch(function () { throw {}; });
            })
            .then(function () {
                state.verifiedPassword = password;
                const info = infoFromDom();
                if (info.hasOtp) state.phase = 'otp';
                else if (info.senderPlan === 'free') { state.phase = 'ad'; startAdTimer(); trackAdView(); }
                else state.phase = 'file';
            })
            .catch(function (e) {
                show('dl-password-error'); text('dl-password-error', e.message || (e.errors && e.errors[0] && e.errors[0].message) || t('passwordWrong'));
            })
            .finally(function () { text('dl-password-btn-text', t('passwordUnlock')); removeAttr('dl-password-btn', 'disabled'); render(); });
    }

    function startAdTimer() {
        state.adCountdown = 5;
        text('dl-ad-countdown', t('adCountdown', 5));
        render();
        state.adTimer = setInterval(function () {
            state.adCountdown--;
            text('dl-ad-countdown', t('adCountdown', state.adCountdown));
            if (state.adCountdown <= 0) {
                clearInterval(state.adTimer!);
                state.adTimer = null;
                state.phase = 'file';
                render();
            }
        }, 1000);
    }
    function skipAd() { if (state.adTimer) { clearInterval(state.adTimer); state.adTimer = null; } state.phase = 'file'; render(); }

    function toggleZK() { state.zkOpen = !state.zkOpen; render(); }

    function downloadUrl(): string {
        let url = apiBase() + '/api/downloads/' + shareCode();
        const params: string[] = [];
        if (state.verifiedPassword) params.push('password=' + encodeURIComponent(state.verifiedPassword));
        if (state.verifiedOtp) params.push('otp=' + encodeURIComponent(state.verifiedOtp));
        if (params.length) url += '?' + params.join('&');
        return url;
    }

    // Returns false to cancel the anchor's own navigation, true to let it
    // happen. With JS dead the anchor still delivers an unencrypted file.
    function handleDownload(event?: Event): boolean | void {
        // Viral CTA (issue #15): reveal once the recipient's hands are free —
        // after the download starts, never before.
        show('dl-viral-cta');
        if (state.bytes) { saveBytes(state.bytes); return false; }
        if (!state.isEncrypted && !state.verifiedOtp && !state.verifiedPassword) return true;
        if (event && event.preventDefault) event.preventDefault();
        state.downloading = true; render();
        if (state.isEncrypted) {
            fetchBytes().then(saveBytes)['catch'](function (e) {
                if (window.DAUB) window.DAUB.toast(t('downloadFailed') + ': ' + e.message, 'error');
            })['finally'](function () { state.downloading = false; render(); });
        } else {
            state.downloading = false;
            window.location.href = downloadUrl();
        }
        return false;
    }

    function saveBytes(bytes: ArrayBuffer) { const blob = new Blob([bytes]); const dlUrl = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = dlUrl; a.download = infoFromDom().fileName || shareCode(); document.body.appendChild(a); a.click(); document.body.removeChild(a); URL.revokeObjectURL(dlUrl); }

    async function fetchBytes(): Promise<ArrayBuffer> { const response = await fetch(downloadUrl()); if (!response.ok) throw new Error('Download failed: ' + response.statusText); const data = await response.arrayBuffer(); if (!state.isEncrypted) return data; const hash = window.location.hash; const keyMatch = hash.match(/key=([A-Za-z0-9\-_]+)/); if (!keyMatch) throw new Error('Encryption key not found'); let base64 = keyMatch[1].replace(/-/g, '+').replace(/_/g, '/'); while (base64.length % 4) base64 += '='; const binary = atob(base64); const keyBytes = new Uint8Array(binary.length); for (let i = 0; i < binary.length; i++) keyBytes[i] = binary.charCodeAt(i); const key = await crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['decrypt']); const nonce = data.slice(0, 12); const ciphertext = data.slice(12); return await crypto.subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(nonce) }, key, ciphertext); }

    function maybeRenderTextPreview() {
        if (state.previewStarted) return;
        const i = infoFromDom();
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

    function renderText(truncate: boolean) {
        const body = document.getElementById('dl-text-body');
        if (!body) return;
        const lines = state.fullText!.split('\n');
        const truncated = truncate && lines.length > PREVIEW_LINE_LIMIT;
        // textContent, never innerHTML — the snippet is untrusted user content.
        body.textContent = truncated ? lines.slice(0, PREVIEW_LINE_LIMIT).join('\n') : state.fullText;
        if (truncated) { show('dl-text-more'); text('dl-text-status', t('previewLines', PREVIEW_LINE_LIMIT, lines.length)); }
        else { hide('dl-text-more'); text('dl-text-status', ''); }
    }

    function showFullText() { renderText(false); }

    // Media preview (issue #21): image/video/audio/PDF inline, served by the
    // preview endpoint (never counts a download). Skipped for encrypted or
    // OTP-gated deliveries — the payload there is not previewable. Elements
    // are built via createElement so nothing user-controlled becomes markup.
    function maybeRenderMediaPreview() {
        if (state.mediaPreviewStarted) return;
        const i = infoFromDom();
        const ct = String(i.contentType || '').toLowerCase();
        const kind = ct.indexOf('image/') === 0 ? 'image'
            : ct.indexOf('video/') === 0 ? 'video'
            : ct.indexOf('audio/') === 0 ? 'audio'
            : ct === 'application/pdf' ? 'pdf' : null;
        if (!kind || state.isEncrypted || i.hasOtp || i.hasPassword) return;

        const container = document.getElementById('dl-media-preview');
        if (!container) return;
        state.mediaPreviewStarted = true;

        const url = apiBase() + '/api/downloads/' + shareCode() + '/preview';
        const el = document.createElement(kind === 'image' ? 'img' : kind === 'video' ? 'video' : kind === 'audio' ? 'audio' : 'iframe') as HTMLElement;
        el.style.maxWidth = '100%';
        el.style.display = 'block';
        el.style.margin = '0 auto';
        if (kind !== 'audio') el.style.maxHeight = '60vh';

        if (kind === 'image') {
            el.setAttribute('src', url);
            el.setAttribute('alt', t('previewImageAlt'));
        } else if (kind === 'video') {
            el.setAttribute('src', url);
            el.setAttribute('controls', '');
            el.setAttribute('playsinline', '');
            el.setAttribute('preload', 'metadata');
        } else if (kind === 'audio') {
            el.setAttribute('src', url);
            el.setAttribute('controls', '');
            el.setAttribute('preload', 'metadata');
        } else {
            el.setAttribute('src', url);
            el.setAttribute('title', t('previewPdfTitle'));
            el.style.height = '65vh';
            el.style.border = '0';
            el.style.background = '#fff';
        }

        container.appendChild(el);
        show('dl-media-preview');
    }

    function copyText() {
        if (!state.fullText || !navigator.clipboard) return;
        navigator.clipboard.writeText(state.fullText).then(function () {
            text('dl-text-copy-label', t('copied'));
            setTimeout(function () { text('dl-text-copy-label', t('copy')); }, 2000);
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
    return { verifyOtp: verifyOtp, verifyPassword: verifyPassword, passwordKeydown: passwordKeydown, otpInput: otpInput, otpKeydown: otpKeydown, skipAd: skipAd, toggleZK: toggleZK, handleDownload: handleDownload, showFullText: showFullText, copyText: copyText };
})();