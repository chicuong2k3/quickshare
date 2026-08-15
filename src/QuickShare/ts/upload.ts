// QuickShareCloud upload page logic — TypeScript port (IIFE, no modules).
// R2 Multipart Upload + Client-Side AES-256-GCM Encryption
(function () {
    'use strict';

    // Copy comes from the server so this file carries no English literals.
    var T: Record<string, string> = (function () {
        var el = document.getElementById('upload-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    })();

    function t(key: string, a?: string | number, b?: string | number): string {
        var v = T[key] || '';
        return v.replace('{0}', String(a)).replace('{1}', String(b));
    }

    var MAX_FILE_BYTES = 5 * 1024 * 1024 * 1024;
    var PART_TARGET = 25 * 1024 * 1024;
    var CONCURRENT_PARTS = 4;
    var PART_RETRY_ATTEMPTS = 3;
    var SOURCE_WEB = 'web';
    var SOURCE_PASTE = 'paste';

    interface UploadContext {
        fileName: string;
        contentType: string;
        originalSize: number;
        uploadBlob: Blob;
        effectiveSize: number;
        totalParts: number;
        partSize: number;
        partEtags: Array<{ partNumber: number; etag: string }>;
        urlByPart: Record<number, string>;
        deliveryId: string;
        uploadId: string;
        objectKey: string;
        encryptionKeyStr: string | null;
    }

    var state: {
        phase: string;
        progress: number;
        filename: string;
        link: string;
        expiresAt: string;
        error: string;
        copied: boolean;
        pasteTip: boolean;
        draggingActive: boolean;
        encryptionEnabled: boolean;
        _encryptionUsed: boolean;
        otp: string;
        _abortController: AbortController | null;
        _deliveryId: string | null;
        _uploadId: string | null;
        _cancelled: boolean;
        fileSize: number;
        uploadedBytes: number;
        speed: string;
        eta: string;
        _startTime: number;
        source: string;
        _resumeCtx: UploadContext | null;
        objectKey: string;
    } = {
        phase: 'idle',
        progress: 0, filename: '', link: '', expiresAt: '', error: '',
        copied: false, pasteTip: false, draggingActive: false,
        encryptionEnabled: true, _encryptionUsed: false,
        otp: '',
        _abortController: null, _deliveryId: null, _uploadId: null, _cancelled: false,
        fileSize: 0, uploadedBytes: 0, speed: '', eta: '', _startTime: 0,
        source: SOURCE_WEB,
        _resumeCtx: null, objectKey: ''
    };

    function show(id: string): void { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id: string): void { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }

    function render(): void {
        ['upload-idle', 'upload-uploading', 'upload-success', 'upload-error'].forEach(hide);
        var phaseMap: Record<string, string> = { idle: 'upload-idle', dragging: 'upload-idle', uploading: 'upload-uploading', success: 'upload-success', error: 'upload-error' };
        var elId = phaseMap[state.phase]; if (elId) show(elId);

        if (state.phase === 'idle' || state.phase === 'dragging') {
            var isDrag = state.draggingActive;
            var idleText = document.getElementById('upload-idle-text');
            var dragText = document.getElementById('upload-drag-text');
            var idleSub = document.getElementById('upload-idle-subtext');
            var dragSub = document.getElementById('upload-drag-subtext');
            var dropIdle = document.getElementById('dropzone-idle');
            var dropDrag = document.getElementById('dropzone-drag');
            if (isDrag) {
                if (idleText) idleText.classList.add('hidden');
                if (dragText) dragText.classList.remove('hidden');
                if (idleSub) idleSub.classList.add('hidden');
                if (dragSub) dragSub.classList.remove('hidden');
                if (dropIdle) dropIdle.classList.add('hidden');
                if (dropDrag) dropDrag.classList.remove('hidden');
            } else {
                if (idleText) idleText.classList.remove('hidden');
                if (dragText) dragText.classList.add('hidden');
                if (idleSub) idleSub.classList.remove('hidden');
                if (dragSub) dragSub.classList.add('hidden');
                if (dropIdle) dropIdle.classList.remove('hidden');
                if (dropDrag) dropDrag.classList.add('hidden');
            }
            var dz = document.getElementById('dropzone');
            if (dz) { if (state.draggingActive) dz.classList.add('active'); else dz.classList.remove('active'); }
        }
        if (state.phase === 'uploading') {
            var filenameEl = document.getElementById('upload-filename');
            var progressLabelEl = document.getElementById('upload-progress-label');
            var progressBar = document.getElementById('upload-progress-bar');
            if (filenameEl) filenameEl.textContent = state.filename;
            if (progressLabelEl) progressLabelEl.textContent = progressLabel();
            if (progressBar) progressBar.setAttribute('aria-valuenow', String(state.progress));
            show('upload-encrypt-badge');
        }
        if (state.phase === 'success') {
            var successNameEl = document.getElementById('upload-success-name');
            var shareLinkEl = document.getElementById('share-link') as HTMLInputElement | null;
            if (successNameEl) successNameEl.textContent = state.filename;
            if (shareLinkEl) shareLinkEl.value = state.link;
            show('upload-success-encrypt-badge');
        }
        if (state.phase === 'error') {
            var errorMsgEl = document.getElementById('upload-error-msg');
            if (errorMsgEl) errorMsgEl.textContent = state.error || t('errorUnknown');
            if (state._resumeCtx) show('upload-resume-btn'); else hide('upload-resume-btn');
        }

        // Paste tip
        if (state.pasteTip) show('upload-paste-tip'); else hide('upload-paste-tip');

        // Expiry label — date comes from the server (result.expiresAt), not a client choice.
        var expLabel = document.getElementById('upload-expiry-label');
        if (expLabel && state.expiresAt) {
            try {
                var d = new Date(state.expiresAt);
                if (!isNaN(d.getTime())) expLabel.textContent = t('expiryLabel', d.toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }));
            } catch (_) { /* invalid expiry string; keep the default label */ }
        }
    }

    function apiBase(): string { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }

    function formatBytes(b: number): string { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }

    function progressLabel(): string {
        if (state.progress === 0) return t('starting');
        var l = state.progress + '%';
        if (state.uploadedBytes && state.fileSize) l += ' \u00b7 ' + formatBytes(state.uploadedBytes) + ' / ' + formatBytes(state.fileSize);
        if (state.speed) l += ' \u00b7 ' + state.speed;
        if (state.eta) l += ' \u00b7 ~' + state.eta;
        return l;
    }

    window.uploadPage = {
        otpInput: function (event, idx) {
            var target = event.target as HTMLInputElement;
            var val = target.value.replace(/\D/g, '');
            target.value = val;
            if (val && idx < 5) {
                var next = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input')[idx + 1];
                if (next) next.focus();
            }
        },
        otpKeydown: function (event, idx) {
            var target = event.target as HTMLInputElement;
            if (event.key === 'Backspace' && !target.value && idx > 0) {
                var prev = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input')[idx - 1];
                if (prev) prev.focus();
            }
        },
        readOtp: function () {
            var inputs = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input');
            var pin = '';
            for (var i = 0; i < inputs.length; i++) pin += inputs[i].value;
            state.otp = pin;
        },

        onDragOver: function (e) {
            e.preventDefault();
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
            state.draggingActive = true;
            if (state.phase === 'idle') state.phase = 'dragging';
            render();
        },
        onDragLeave: function (e) {
            e.preventDefault();
            state.draggingActive = false;
            if (state.phase === 'dragging') state.phase = 'idle';
            render();
        },
        onDrop: function (e) {
            e.preventDefault();
            state.draggingActive = false;
            var files = e.dataTransfer && e.dataTransfer.files;
            if (files && files.length) {
                startUpload(files[0]);
            } else {
                state.phase = 'idle';
                render();
            }
        },

        onFileInput: function (e) {
            var target = e.target as HTMLInputElement;
            var files = target && target.files;
            if (files && files.length) startUpload(files[0]);
        },
        triggerFileInput: function () { var input = document.querySelector<HTMLInputElement>('input[type="file"]'); if (input) input.click(); },

        copyLink: function () {
            if (!state.link) return;
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(state.link).then(function () { state.copied = true; render(); }, function () { copyFallback(); });
            } else { copyFallback(); }
        },

        cancelUpload: function () {
            if (state._abortController) state._abortController.abort();
            state._cancelled = true;
            if (state._deliveryId && state._uploadId) {
                fetch(apiBase() + '/api/uploads/abort-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ deliveryId: state._deliveryId, uploadId: state._uploadId }) }).catch(function () { /* abort is best-effort; ignore network failures */ });
            }
            reset();
            render();
        },

        resumeUpload: function () {
            var ctx = state._resumeCtx; if (!ctx) return;
            state.phase = 'uploading'; state.error = ''; state._cancelled = false;
            state._abortController = new AbortController(); state._startTime = Date.now();
            render();
            resumePendingParts(ctx).catch(function (e) { if (state._abortController?.signal.aborted || state._cancelled) return; state.phase = 'error'; state.error = (e as Error).message || t('failed'); render(); });
        },

        reset: function () { reset(); render(); }
    };

    function copyFallback(): void {
        var input = document.getElementById('share-link') as HTMLInputElement | null; if (!input) return;
        try { input.removeAttribute('readonly'); input.focus(); input.select(); document.execCommand('copy'); state.copied = true; render(); } catch (_) { state.copied = false; } finally { input.setAttribute('readonly', ''); }
    }

    function reset(): void {
        state.phase = 'idle'; state.progress = 0; state.filename = ''; state.link = ''; state.expiresAt = '';
        state.error = ''; state.copied = false; state.encryptionEnabled = true; state._encryptionUsed = false; state.otp = '';
        state._abortController = null; state._deliveryId = null; state._uploadId = null; state._resumeCtx = null;
        state.uploadedBytes = 0; state.fileSize = 0; state.speed = ''; state.eta = '';
        state.source = SOURCE_WEB;
    }

    function sleep(ms: number): Promise<void> { return new Promise<void>(function (res) { setTimeout(res, ms); }); }

    function startUpload(file: File, source?: string): void {
        if (!file) return;
        if (file.size > MAX_FILE_BYTES) { state.phase = 'error'; state.error = t('sizeExceeded'); render(); return; }
        state.source = source || SOURCE_WEB;
        state.filename = file.name || 'pasted-image';
        state.fileSize = file.size; state.progress = 0; state.error = ''; state.link = '';
        state.copied = false; state._encryptionUsed = false; state._cancelled = false;
        state.phase = 'uploading'; state._startTime = Date.now(); render();
        doUpload(file).catch(function (e) { if (state._abortController?.signal.aborted || state._cancelled) return; state.phase = 'error'; state.error = (e as Error).message || t('failed'); render(); });
    }

    async function doUpload(file: File): Promise<void> {
        var api = apiBase();
        var uploadBlob: Blob = file;
        var encryptionKeyStr: string | null = null;
        var originalSize = file.size;

        if (state.encryptionEnabled) {
            var keyInfo = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
            var raw = await crypto.subtle.exportKey('raw', keyInfo);
            var rawKey = new Uint8Array(raw);
            var fileData = await file.arrayBuffer();
            var nonce = crypto.getRandomValues(new Uint8Array(12));
            var encryptedBuf = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce }, keyInfo, fileData);
            var combined = new Uint8Array(nonce.length + encryptedBuf.byteLength);
            combined.set(nonce, 0); combined.set(new Uint8Array(encryptedBuf), nonce.length);
            uploadBlob = new Blob([combined.buffer as ArrayBuffer]);
            var binary = ''; for (var i = 0; i < rawKey.length; i++) binary += String.fromCharCode(rawKey[i]);
            encryptionKeyStr = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
            state._encryptionUsed = true;
        }

        var effectiveSize = uploadBlob.size;
        var totalParts = effectiveSize <= PART_TARGET ? 1 : Math.ceil(effectiveSize / PART_TARGET);
        var partNumbers: number[] = []; for (var p = 1; p <= totalParts; p++) partNumbers.push(p);

        state._abortController = new AbortController(); var signal = state._abortController!.signal;

        var startResp = await fetch(api + '/api/uploads/start-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ fileName: file.name, fileSize: originalSize, contentType: file.type || 'application/octet-stream', partNumbers: partNumbers }) });
        if (!startResp.ok) { var errT = await startResp.text(); throw new Error(errT || t('startFailed')); }
        var session = await startResp.json();
        state._deliveryId = session.deliveryId; state._uploadId = session.uploadId; state.objectKey = session.objectKey;

        var ctx: UploadContext = {
            fileName: file.name, contentType: file.type || 'application/octet-stream',
            originalSize: originalSize, uploadBlob: uploadBlob, effectiveSize: effectiveSize,
            totalParts: totalParts, partSize: Math.ceil(effectiveSize / totalParts),
            partEtags: [], urlByPart: {},
            deliveryId: session.deliveryId, uploadId: session.uploadId, objectKey: session.objectKey,
            encryptionKeyStr: encryptionKeyStr
        };
        for (var u = 0; u < session.parts.length; u++) ctx.urlByPart[session.parts[u].partNumber] = session.parts[u].url;
        state._resumeCtx = ctx;

        await uploadPartsAndCommit(ctx);
    }

    function completedCount(ctx: UploadContext): number { var n = 0; for (var i = 0; i < ctx.totalParts; i++) if (ctx.partEtags[i]) n++; return n; }

    function updateProgress(ctx: UploadContext): void {
        var done = completedCount(ctx);
        state.progress = Math.round((done / ctx.totalParts) * 100);
        var elapsed = (Date.now() - state._startTime) / 1000;
        if (elapsed > 0 && done > 0) {
            var uploadedSoFar = (done / ctx.totalParts) * ctx.effectiveSize;
            state.uploadedBytes = uploadedSoFar;
            var bps = uploadedSoFar / elapsed;
            state.speed = formatBytes(bps) + '/s';
            var remaining = (ctx.effectiveSize - uploadedSoFar) / bps;
            state.eta = remaining < 60 ? Math.round(remaining) + 's' : Math.round(remaining / 60) + 'm';
        }
        render();
    }

    async function freshPartUrls(ctx: UploadContext, partNums: number[], signal: AbortSignal): Promise<void> {
        var resp = await fetch(apiBase() + '/api/uploads/part-urls', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ deliveryId: ctx.deliveryId, uploadId: ctx.uploadId, partNumbers: partNums }) });
        if (!resp.ok) { var errText = await resp.text(); throw new Error(errText || t('urlRefreshFailed')); }
        var data = await resp.json();
        for (var i = 0; i < data.parts.length; i++) ctx.urlByPart[data.parts[i].partNumber] = data.parts[i].url;
    }

    async function uploadOnePart(ctx: UploadContext, partNumber: number, signal: AbortSignal): Promise<void> {
        var idx = partNumber - 1;
        var start = idx * ctx.partSize; var end = Math.min(start + ctx.partSize, ctx.effectiveSize);
        var lastError: unknown = null;
        for (var attempt = 1; attempt <= PART_RETRY_ATTEMPTS; attempt++) {
            try {
                var resp = await fetch(ctx.urlByPart[partNumber], { method: 'PUT', body: ctx.uploadBlob.slice(start, end), signal: signal });
                if (!resp.ok) throw new Error(t('partFailed', partNumber, resp.status));
                var etag = (resp.headers.get('ETag') || '').replace(/^"(.*)"$/, '$1');
                ctx.partEtags[idx] = { partNumber: partNumber, etag: etag };
                updateProgress(ctx);
                return;
            } catch (e) {
                if (signal.aborted) throw e;
                lastError = e;
                if (attempt < PART_RETRY_ATTEMPTS) {
                    await sleep(1000 * Math.pow(2, attempt - 1));
                    // Presigned URLs expire; get a fresh one before retrying. If the
                    // refresh itself fails, retry with the old URL rather than give up.
                    try { await freshPartUrls(ctx, [partNumber], signal); } catch (_) { /* refresh failed; retry with the old URL */ }
                }
            }
        }
        throw lastError;
    }

    async function uploadPartsAndCommit(ctx: UploadContext): Promise<void> {
        var signal = state._abortController!.signal;
        var pending: number[] = []; for (var p = 1; p <= ctx.totalParts; p++) if (!ctx.partEtags[p - 1]) pending.push(p);

        for (var batchStart = 0; batchStart < pending.length; batchStart += CONCURRENT_PARTS) {
            var batch = pending.slice(batchStart, batchStart + CONCURRENT_PARTS).map(function (pn) { return uploadOnePart(ctx, pn, signal); });
            var results = await Promise.allSettled(batch);
            for (var r = 0; r < results.length; r++) { var res = results[r]; if (res.status === 'rejected') throw res.reason; }
        }

        var commitResp = await fetch(apiBase() + '/api/uploads/commit-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ deliveryId: ctx.deliveryId, uploadId: ctx.uploadId, objectKey: ctx.objectKey, fileName: ctx.fileName, contentType: ctx.contentType, fileSize: ctx.originalSize, parts: ctx.partEtags, otp: state.otp || null, source: state.source }) });
        if (!commitResp.ok) { var cErr = await commitResp.text(); throw new Error(cErr || t('finalizeFailed')); }
        var result = await commitResp.json();
        state.link = result.shareLink;
        if (ctx.encryptionKeyStr) state.link += '#key=' + ctx.encryptionKeyStr;
        if (result.expiresAt) { state.expiresAt = result.expiresAt; }
        state._resumeCtx = null;
        state.phase = 'success'; render();
        renderQR(state.link);
        window.uploadPage!.copyLink();
    }

    async function resumePendingParts(ctx: UploadContext): Promise<void> {
        var signal = state._abortController!.signal;
        var pending: number[] = []; for (var p = 1; p <= ctx.totalParts; p++) if (!ctx.partEtags[p - 1]) pending.push(p);
        if (pending.length > 0) await freshPartUrls(ctx, pending, signal);
        await uploadPartsAndCommit(ctx);
    }

    function renderQR(text: string): void {
        var el = document.getElementById('qrcode'); if (!el) return; el.innerHTML = '';
        if (!text || !window.qrcode) { if (!window.qrcode) el.textContent = t('qrNotLoaded'); return; }
        try {
            var qr = window.qrcode(0, 'M'); qr.addData(text); qr.make();
            var moduleCount = qr.getModuleCount(); var cell = 6; var dim = moduleCount * cell;
            var parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="' + t('qrAria') + '" style="image-rendering:pixelated"><rect width="' + dim + '" height="' + dim + '" fill="#fff"/>'];
            for (var r = 0; r < moduleCount; r++) for (var c = 0; c < moduleCount; c++) if (qr.isDark(r, c)) parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
            parts.push('</svg>'); el.innerHTML = parts.join('');
        } catch (e) { el.textContent = t('qrUnavailable'); }
    }

    function flashPasteTip(): void {
        state.pasteTip = true; render();
        setTimeout(function () { state.pasteTip = false; render(); }, 2000);
    }

    document.addEventListener('paste', function (e) {
        if (state.phase === 'uploading') return;
        if (!window.qsPaste || window.qsPaste.isEditable(e.target)) return;
        var file = window.qsPaste.fileFromClipboard(e);
        if (!file) { flashPasteTip(); return; }
        e.preventDefault();
        startUpload(file, SOURCE_PASTE);
    });

    // Pasted on the homepage or a /tools page → the file was stashed before the redirect here.
    function consumePasteHandoff(): void {
        if (!window.qsPaste) return;
        var file = window.qsPaste.takeStashed();
        if (file) { startUpload(file, SOURCE_PASTE); return; }
        if (window.location.hash === '#paste') flashPasteTip();
    }

    render();
    consumePasteHandoff();
})();