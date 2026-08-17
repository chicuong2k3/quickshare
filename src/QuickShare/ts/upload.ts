// QuickShareCloud upload page logic — TypeScript port (IIFE, no modules).
// R2 Multipart Upload + Client-Side AES-256-GCM Encryption
(function () {
    'use strict';

    // Copy comes from the server so this file carries no English literals.
    const T: Record<string, string> = (function () {
        const el = document.getElementById('upload-i18n');
        try { return JSON.parse((el && el.textContent) || '{}'); } catch (_) { return {}; }
    })();

    function t(key: string, a?: string | number, b?: string | number): string {
        const v = T[key] || '';
        return v.replace('{0}', String(a)).replace('{1}', String(b));
    }

    const MAX_FILE_BYTES = 5 * 1024 * 1024 * 1024;
    const PART_TARGET = 25 * 1024 * 1024;
    const CONCURRENT_PARTS = 4;
    const PART_RETRY_ATTEMPTS = 3;
    const SOURCE_WEB = 'web';
    const SOURCE_PASTE = 'paste';
    // Raster formats the canvas can decode + re-encode for metadata strip (#170).
    // GIF is skipped (re-encode drops animation); HEIC is left byte-for-byte.
    const STRIPPABLE_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

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

    const state: {
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
        senderEmail: string;
        deliverAt: string | null;
        message: string;
        password: string;
        sha256: string;
        burnAfterDownload: boolean;
        allowExtensionRequests: boolean;
        stripMetadata: boolean;
        compressImages: boolean;
        expiryHours: number | null;
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
        senderEmail: '',
        deliverAt: null,
        message: '',
        password: '',
        sha256: '',
        burnAfterDownload: false,
        allowExtensionRequests: false,
        stripMetadata: true,
        compressImages: true,
        expiryHours: null,
        _resumeCtx: null, objectKey: ''
    };

    function show(id: string): void { const el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id: string): void { const el = document.getElementById(id); if (el) el.classList.add('hidden'); }

    function render(): void {
        ['upload-idle', 'upload-uploading', 'upload-success', 'upload-error'].forEach(hide);
        const phaseMap: Record<string, string> = { idle: 'upload-idle', dragging: 'upload-idle', uploading: 'upload-uploading', success: 'upload-success', error: 'upload-error' };
        const elId = phaseMap[state.phase]; if (elId) show(elId);

        if (state.phase === 'idle' || state.phase === 'dragging') {
            const isDrag = state.draggingActive;
            const idleText = document.getElementById('upload-idle-text');
            const dragText = document.getElementById('upload-drag-text');
            const idleSub = document.getElementById('upload-idle-subtext');
            const dragSub = document.getElementById('upload-drag-subtext');
            const dropIdle = document.getElementById('dropzone-idle');
            const dropDrag = document.getElementById('dropzone-drag');
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
            const dz = document.getElementById('dropzone');
            if (dz) { if (state.draggingActive) dz.classList.add('active'); else dz.classList.remove('active'); }
        }
        if (state.phase === 'uploading') {
            const filenameEl = document.getElementById('upload-filename');
            const progressLabelEl = document.getElementById('upload-progress-label');
            const progressBar = document.getElementById('upload-progress-bar');
            if (filenameEl) filenameEl.textContent = state.filename;
            if (progressLabelEl) progressLabelEl.textContent = progressLabel();
            if (progressBar) progressBar.setAttribute('aria-valuenow', String(state.progress));
            show('upload-encrypt-badge');
        }
        if (state.phase === 'success') {
            const successNameEl = document.getElementById('upload-success-name');
            const shareLinkEl = document.getElementById('share-link') as HTMLInputElement | null;
            if (successNameEl) successNameEl.textContent = state.filename;
            if (shareLinkEl) shareLinkEl.value = state.link;
            show('upload-success-encrypt-badge');
        }
        if (state.phase === 'error') {
            const errorMsgEl = document.getElementById('upload-error-msg');
            if (errorMsgEl) errorMsgEl.textContent = state.error || t('errorUnknown');
            if (state._resumeCtx) show('upload-resume-btn'); else hide('upload-resume-btn');
        }

        // Paste tip
        if (state.pasteTip) show('upload-paste-tip'); else hide('upload-paste-tip');

        // Expiry label — date comes from the server (result.expiresAt), not a client choice.
        const expLabel = document.getElementById('upload-expiry-label');
        if (expLabel && state.expiresAt) {
            try {
                const d = new Date(state.expiresAt);
                if (!isNaN(d.getTime())) expLabel.textContent = t('expiryLabel', d.toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }));
            } catch (_) { /* invalid expiry string; keep the default label */ }
        }
    }

    function apiBase(): string { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }

    function formatBytes(b: number): string { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }

    function progressLabel(): string {
        if (state.progress === 0) return t('starting');
        let l = state.progress + '%';
        if (state.uploadedBytes && state.fileSize) l += ' \u00b7 ' + formatBytes(state.uploadedBytes) + ' / ' + formatBytes(state.fileSize);
        if (state.speed) l += ' \u00b7 ' + state.speed;
        if (state.eta) l += ' \u00b7 ~' + state.eta;
        return l;
    }

    window.uploadPage = {
        otpInput: function (event, idx) {
            const target = event.target as HTMLInputElement;
            const val = target.value.replace(/\D/g, '');
            target.value = val;
            if (val && idx < 5) {
                const next = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input')[idx + 1];
                if (next) next.focus();
            }
        },
        otpKeydown: function (event, idx) {
            const target = event.target as HTMLInputElement;
            if (event.key === 'Backspace' && !target.value && idx > 0) {
                const prev = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input')[idx - 1];
                if (prev) prev.focus();
            }
        },
        readOtp: function () {
            const inputs = document.querySelectorAll<HTMLInputElement>('#upload-otp .db-otp__input');
            let pin = '';
            for (let i = 0; i < inputs.length; i++) pin += inputs[i].value;
            state.otp = pin;
        },

        // Live strength feedback while typing a share password (issue #287).
        // ponytail: heuristic scoring (length + character variety), not zxcvbn — good
        // enough for a share password; swap in zxcvbn if real entropy estimates matter.
        passwordInput: function (event) {
            const value = (event.target as HTMLInputElement).value;
            const meter = document.getElementById('upload-password-meter');
            if (!meter) return;
            if (!value) { meter.classList.add('hidden'); return; }
            meter.classList.remove('hidden');
            let score = 0;
            if (value.length >= 8) score++;
            if (value.length >= 12) score++;
            if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
            if (/\d/.test(value)) score++;
            if (/[^a-zA-Z0-9]/.test(value)) score++;
            const level = score <= 2 ? 'weak' : score <= 3 ? 'fair' : 'strong';
            meter.setAttribute('data-level', level);
            const fill = meter.querySelector<HTMLElement>('.pw-meter__fill');
            if (fill) fill.style.width = (score * 20) + '%';
            const label = meter.querySelector<HTMLElement>('.pw-meter__label');
            if (label) label.textContent = meter.getAttribute('data-' + level) || '';
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
            const files = e.dataTransfer && e.dataTransfer.files;
            if (files && files.length) {
                startUpload(files[0]);
            } else {
                state.phase = 'idle';
                render();
            }
        },

        onFileInput: function (e) {
            const target = e.target as HTMLInputElement;
            const files = target && target.files;
            if (files && files.length) startUpload(files[0]);
        },
        triggerFileInput: function () { const input = document.querySelector<HTMLInputElement>('input[type="file"]'); if (input) input.click(); },

        // Text snippet sharing (issue #178): paste text, share it like a file.
        toggleTextMode: function () {
            const mode = document.getElementById('upload-text-mode');
            if (!mode) return;
            mode.classList.toggle('hidden');
            if (!mode.classList.contains('hidden')) {
                const ta = document.getElementById('upload-text-input') as HTMLTextAreaElement | null;
                if (ta) ta.focus();
            }
        },
        shareText: function () {
            const ta = document.getElementById('upload-text-input') as HTMLTextAreaElement | null;
            if (!ta || !ta.value || !ta.value.trim()) return;
            const file = new File([ta.value], 'snippet.txt', { type: 'text/plain;charset=utf-8' });
            startUpload(file, SOURCE_WEB);
        },

        copyLink: function () {
            if (!state.link) return;
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(state.link).then(function () { state.copied = true; render(); }, function () { copyFallback(); });
            } else { copyFallback(); }
        },

        // Native share sheet (issue #38): Zalo/WhatsApp/Telegram/email on mobile,
        // copy-link fallback where Web Share API is unavailable.
        shareLink: function () {
            if (!state.link) return;
            if (navigator.share) {
                navigator.share({ title: t('shareTitle'), text: t('shareText'), url: state.link }).catch(function () { /* user dismissed the sheet */ });
            } else {
                window.uploadPage!.copyLink();
            }
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
            const ctx = state._resumeCtx; if (!ctx) return;
            state.phase = 'uploading'; state.error = ''; state._cancelled = false;
            state._abortController = new AbortController(); state._startTime = Date.now();
            render();
            resumePendingParts(ctx).catch(function (e) { if (state._abortController?.signal.aborted || state._cancelled) return; state.phase = 'error'; state.error = (e as Error).message || t('failed'); render(); });
        },

        reset: function () { reset(); render(); }
    };

    function copyFallback(): void {
        const input = document.getElementById('share-link') as HTMLInputElement | null; if (!input) return;
        try { input.removeAttribute('readonly'); input.focus(); input.select(); document.execCommand('copy'); state.copied = true; render(); } catch (_) { state.copied = false; } finally { input.setAttribute('readonly', ''); }
    }

    function reset(): void {
        state.phase = 'idle'; state.progress = 0; state.filename = ''; state.link = ''; state.expiresAt = '';
        state.error = ''; state.copied = false; state.encryptionEnabled = true; state._encryptionUsed = false; state.otp = '';
        state._abortController = null; state._deliveryId = null; state._uploadId = null; state._resumeCtx = null;
        state.uploadedBytes = 0; state.fileSize = 0; state.speed = ''; state.eta = '';
        state.source = SOURCE_WEB;
        state.senderEmail = '';
        state.deliverAt = null;
        state.message = '';
        state.expiryHours = null;
        const senderEmailEl = document.getElementById('upload-sender-email') as HTMLInputElement | null;
        if (senderEmailEl)
            senderEmailEl.value = '';
    }

    function sleep(ms: number): Promise<void> { return new Promise<void>(function (res) { setTimeout(res, ms); }); }

    function startUpload(file: File, source?: string): void {
        if (!file) return;
        if (file.size > MAX_FILE_BYTES) { state.phase = 'error'; state.error = t('sizeExceeded'); render(); return; }
        state.source = source || SOURCE_WEB;
        state.senderEmail = (document.getElementById('upload-sender-email') as HTMLInputElement | null)?.value.trim() ?? '';
        const deliverAtInput = document.getElementById('upload-deliver-at') as HTMLInputElement | null;
        state.deliverAt = deliverAtInput && deliverAtInput.value ? new Date(deliverAtInput.value).toISOString() : null;
        state.message = (document.getElementById('upload-message') as HTMLTextAreaElement | null)?.value.trim() ?? '';
        state.password = (document.getElementById('upload-password') as HTMLInputElement | null)?.value ?? '';
        state.burnAfterDownload = (document.getElementById('upload-burn') as HTMLInputElement | null)?.checked ?? false;
state.allowExtensionRequests = (document.getElementById('upload-allow-requests') as HTMLInputElement | null)?.checked ?? false;
        state.stripMetadata = (document.getElementById('upload-strip-metadata') as HTMLInputElement | null)?.checked ?? true;
        state.compressImages = (document.getElementById('upload-compress') as HTMLInputElement | null)?.checked ?? true;
        const expirySelect = document.getElementById('upload-expiry') as HTMLSelectElement | null;
        const expiryDays = document.getElementById('upload-expiry-days') as HTMLInputElement | null;
        if (expirySelect) state.expiryHours = parseFloat(expirySelect.value) || null;
        else if (expiryDays) state.expiryHours = (parseFloat(expiryDays.value) || 0) * 24;
        state.filename = file.name || 'pasted-image';
        state.fileSize = file.size; state.progress = 0; state.error = ''; state.link = '';
        state.copied = false; state._encryptionUsed = false; state._cancelled = false;
        state.phase = 'uploading'; state._startTime = Date.now(); render();

        // Auto-compress images (issue #63) before the upload starts. For non-images
        // (or when the toggle is off) this resolves immediately with the same file.
        maybeCompress(file).then(function (out) {
            state.filename = out.name;
            state.fileSize = out.size;
            render();
            doUpload(out).catch(function (e) { if (state._abortController?.signal.aborted || state._cancelled) return; state.phase = 'error'; state.error = (e as Error).message || t('failed'); render(); });
        });
    }

    function maybeCompress(file: File): Promise<File> {
        if (!state.compressImages) return Promise.resolve(file);
        if (!file.type || file.type.indexOf('image/') !== 0) return Promise.resolve(file);
        // GIF (animated) and SVG (vector) are left untouched — rasterizing them loses more than it saves.
        if (file.type === 'image/gif' || file.type === 'image/svg+xml') return Promise.resolve(file);
        return compressImage(file);
    }

    function compressImage(file: File): Promise<File> {
        return new Promise(function (resolve) {
            const MAX_DIM = 1920;
            const MIN_BYTES = 1024 * 1024;
            if (file.size <= MIN_BYTES) { resolve(file); return; }

            const img = new Image();
            const url = URL.createObjectURL(file);
            img.onload = function () {
                URL.revokeObjectURL(url);
                const w = img.naturalWidth, h = img.naturalHeight;
                if (w <= MAX_DIM && h <= MAX_DIM) { resolve(file); return; }
                const scale = Math.min(1, MAX_DIM / Math.max(w, h));
                const outW = Math.round(w * scale), outH = Math.round(h * scale);
                const canvas = document.createElement('canvas');
                canvas.width = outW; canvas.height = outH;
                const ctx = canvas.getContext('2d');
                if (!ctx) { resolve(file); return; }
                ctx.drawImage(img, 0, 0, outW, outH);
                canvas.toBlob(function (blob) {
                    if (!blob || blob.size >= file.size) { resolve(file); return; }
                    const name = file.name.replace(/\.[^.]+$/, '') + '.jpg';
                    resolve(new File([blob], name, { type: 'image/jpeg', lastModified: file.lastModified }));
                }, 'image/jpeg', 0.82);
            };
            img.onerror = function () { URL.revokeObjectURL(url); resolve(file); };
            img.src = url;
        });
    }

    async function doUpload(file: File): Promise<void> {
        const api = apiBase();
        let uploadBlob: Blob = file;
        let encryptionKeyStr: string | null = null;
        const sourceName = file.name || 'pasted-image';
        let sourceType = file.type || 'application/octet-stream';
        let sourceFile: Blob = file;

        // Privacy-first metadata strip (issue #170): re-encode raster images so
        // GPS/camera/timestamp tags never reach storage. Orientation preserved via
        // createImageBitmap; the #66 SHA-256 is computed on the stripped bytes.
        // Best-effort — a decode failure uploads the original, never blocks.
        if (state.stripMetadata && STRIPPABLE_IMAGE_TYPES.indexOf(file.type.toLowerCase()) >= 0) {
            try {
                sourceFile = await stripImageMetadata(file);
                sourceType = sourceFile.type || sourceType;
            } catch { /* keep original */ }
        }

        const originalSize = sourceFile.size;
        const fileData = await sourceFile.arrayBuffer();
        state.sha256 = await sha256Hex(fileData);

        if (state.encryptionEnabled) {
            const keyInfo = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
            const raw = await crypto.subtle.exportKey('raw', keyInfo);
            const rawKey = new Uint8Array(raw);
            const nonce = crypto.getRandomValues(new Uint8Array(12));
            const encryptedBuf = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce }, keyInfo, fileData);
            const combined = new Uint8Array(nonce.length + encryptedBuf.byteLength);
            combined.set(nonce, 0); combined.set(new Uint8Array(encryptedBuf), nonce.length);
            uploadBlob = new Blob([combined.buffer as ArrayBuffer]);
            let binary = ''; for (let i = 0; i < rawKey.length; i++) binary += String.fromCharCode(rawKey[i]);
            encryptionKeyStr = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
            state._encryptionUsed = true;
        }

        const effectiveSize = uploadBlob.size;
        const totalParts = effectiveSize <= PART_TARGET ? 1 : Math.ceil(effectiveSize / PART_TARGET);
        const partNumbers: number[] = []; for (let p = 1; p <= totalParts; p++) partNumbers.push(p);

        state._abortController = new AbortController(); const signal = state._abortController!.signal;

        const startResp = await fetch(api + '/api/uploads/start-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ fileName: sourceName, fileSize: originalSize, contentType: sourceType, partNumbers: partNumbers, senderEmail: state.senderEmail || null }) });
        if (!startResp.ok) { const errT = await startResp.text(); throw new Error(errT || t('startFailed')); }
        const session = await startResp.json();
        state._deliveryId = session.deliveryId; state._uploadId = session.uploadId; state.objectKey = session.objectKey;

        const ctx: UploadContext = {
            fileName: sourceName, contentType: sourceType,
            originalSize: originalSize, uploadBlob: uploadBlob, effectiveSize: effectiveSize,
            totalParts: totalParts, partSize: Math.ceil(effectiveSize / totalParts),
            partEtags: [], urlByPart: {},
            deliveryId: session.deliveryId, uploadId: session.uploadId, objectKey: session.objectKey,
            encryptionKeyStr: encryptionKeyStr
        };
        for (let u = 0; u < session.parts.length; u++) ctx.urlByPart[session.parts[u].partNumber] = session.parts[u].url;
        state._resumeCtx = ctx;

        await uploadPartsAndCommit(ctx);
    }

    async function sha256Hex(buffer: ArrayBuffer): Promise<string> {
        const digest = await crypto.subtle.digest('SHA-256', buffer);
        const bytes = new Uint8Array(digest);
        let hex = '';
        for (let i = 0; i < bytes.length; i++) hex += bytes[i].toString(16).padStart(2, '0');
        return hex;
    }


    async function stripImageMetadata(file: File): Promise<Blob> {
        // 'from-image' bakes the EXIF orientation into the pixels so the output is
        // upright; the canvas re-encode itself drops every metadata tag.
        const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
        try {
            const canvas = document.createElement('canvas');
            canvas.width = bitmap.width;
            canvas.height = bitmap.height;
            const ctx = canvas.getContext('2d');
            if (!ctx) throw new Error('2d context unavailable');
            ctx.drawImage(bitmap, 0, 0);

            const isPng = file.type.toLowerCase() === 'image/png';
            const mime = isPng ? 'image/png' : 'image/jpeg';
            const quality = isPng ? undefined : 0.92;

            return await new Promise<Blob>(function (resolve, reject) {
                canvas.toBlob(function (b) { b ? resolve(b) : reject(new Error('encode failed')); }, mime, quality);
            });
        } finally {
            bitmap.close();
        }
    }

    function completedCount(ctx: UploadContext): number { let n = 0; for (let i = 0; i < ctx.totalParts; i++) if (ctx.partEtags[i]) n++; return n; }

    function updateProgress(ctx: UploadContext): void {
        const done = completedCount(ctx);
        state.progress = Math.round((done / ctx.totalParts) * 100);
        const elapsed = (Date.now() - state._startTime) / 1000;
        if (elapsed > 0 && done > 0) {
            const uploadedSoFar = (done / ctx.totalParts) * ctx.effectiveSize;
            state.uploadedBytes = uploadedSoFar;
            const bps = uploadedSoFar / elapsed;
            state.speed = formatBytes(bps) + '/s';
            const remaining = (ctx.effectiveSize - uploadedSoFar) / bps;
            state.eta = remaining < 60 ? Math.round(remaining) + 's' : Math.round(remaining / 60) + 'm';
        }
        render();
    }

    async function freshPartUrls(ctx: UploadContext, partNums: number[], signal: AbortSignal): Promise<void> {
        const resp = await fetch(apiBase() + '/api/uploads/part-urls', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ deliveryId: ctx.deliveryId, uploadId: ctx.uploadId, partNumbers: partNums }) });
        if (!resp.ok) { const errText = await resp.text(); throw new Error(errText || t('urlRefreshFailed')); }
        const data = await resp.json();
        for (let i = 0; i < data.parts.length; i++) ctx.urlByPart[data.parts[i].partNumber] = data.parts[i].url;
    }

    async function uploadOnePart(ctx: UploadContext, partNumber: number, signal: AbortSignal): Promise<void> {
        const idx = partNumber - 1;
        const start = idx * ctx.partSize; const end = Math.min(start + ctx.partSize, ctx.effectiveSize);
        let lastError: unknown = null;
        for (let attempt = 1; attempt <= PART_RETRY_ATTEMPTS; attempt++) {
            try {
                const resp = await fetch(ctx.urlByPart[partNumber], { method: 'PUT', body: ctx.uploadBlob.slice(start, end), signal: signal });
                if (!resp.ok) throw new Error(t('partFailed', partNumber, resp.status));
                const etag = (resp.headers.get('ETag') || '').replace(/^"(.*)"$/, '$1');
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
        const signal = state._abortController!.signal;
        const pending: number[] = []; for (let p = 1; p <= ctx.totalParts; p++) if (!ctx.partEtags[p - 1]) pending.push(p);

        for (let batchStart = 0; batchStart < pending.length; batchStart += CONCURRENT_PARTS) {
            const batch = pending.slice(batchStart, batchStart + CONCURRENT_PARTS).map(function (pn) { return uploadOnePart(ctx, pn, signal); });
            const results = await Promise.allSettled(batch);
            for (let r = 0; r < results.length; r++) { const res = results[r]; if (res.status === 'rejected') throw res.reason; }
        }

        const commitResp = await fetch(apiBase() + '/api/uploads/commit-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ deliveryId: ctx.deliveryId, uploadId: ctx.uploadId, objectKey: ctx.objectKey, fileName: ctx.fileName, contentType: ctx.contentType, fileSize: ctx.originalSize, parts: ctx.partEtags, otp: state.otp || null, password: state.password || null, message: state.message || null, source: state.source, deliverAt: state.deliverAt, expiryHours: state.expiryHours, sha256: state.sha256 || null, burnAfterDownload: state.burnAfterDownload, allowExtensionRequests: state.allowExtensionRequests }) });
        if (!commitResp.ok) { const cErr = await commitResp.text(); throw new Error(cErr || t('finalizeFailed')); }
        const result = await commitResp.json();
        state.link = result.shareLink;
        if (ctx.encryptionKeyStr) state.link += '#key=' + ctx.encryptionKeyStr;
        if (result.expiresAt) { state.expiresAt = result.expiresAt; }
        state._resumeCtx = null;
        state.phase = 'success'; render();
        renderQR(state.link);
        window.uploadPage!.copyLink();
    }

    async function resumePendingParts(ctx: UploadContext): Promise<void> {
        const signal = state._abortController!.signal;
        const pending: number[] = []; for (let p = 1; p <= ctx.totalParts; p++) if (!ctx.partEtags[p - 1]) pending.push(p);
        if (pending.length > 0) await freshPartUrls(ctx, pending, signal);
        await uploadPartsAndCommit(ctx);
    }

    function renderQR(text: string): void {
        const el = document.getElementById('qrcode'); if (!el) return; el.innerHTML = '';
        if (!text || !window.qrcode) { if (!window.qrcode) el.textContent = t('qrNotLoaded'); return; }
        try {
            const qr = window.qrcode(0, 'M'); qr.addData(text); qr.make();
            const moduleCount = qr.getModuleCount(); const cell = 6; const dim = moduleCount * cell;
            const parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="' + dim + '" height="' + dim + '" viewBox="0 0 ' + dim + ' ' + dim + '" role="img" aria-label="' + t('qrAria') + '" style="image-rendering:pixelated"><rect width="' + dim + '" height="' + dim + '" fill="#fff"/>'];
            for (let r = 0; r < moduleCount; r++) for (let c = 0; c < moduleCount; c++) if (qr.isDark(r, c)) parts.push('<rect x="' + (c * cell) + '" y="' + (r * cell) + '" width="' + cell + '" height="' + cell + '" fill="#0f172a"/>');
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
        const file = window.qsPaste.fileFromClipboard(e);
        if (!file) { flashPasteTip(); return; }
        e.preventDefault();
        startUpload(file, SOURCE_PASTE);
    });

    // Pasted on the homepage or a /tools page → the file was stashed before the redirect here.
    function consumePasteHandoff(): void {
        if (!window.qsPaste) return;
        const file = window.qsPaste.takeStashed();
        if (file) { startUpload(file, SOURCE_PASTE); return; }
        if (window.location.hash === '#paste') flashPasteTip();
    }

    // Android/Chrome Share Target (issue #14): the server stashed the shared
    // file and redirected to /send?shared={id}. Fetch it and run the normal
    // upload flow — progress + resume stay intact.
    function consumeShareTarget(): void {
        const m = window.location.search.match(/[?&]shared=([0-9a-f-]+)/i);
        if (!m) return;
        const id = m[1];
        fetch(apiBase() + '/api/share-target/' + id)
            .then(function (r) {
                if (!r.ok) throw new Error('share-target missing or expired');
                const cd = r.headers.get('Content-Disposition') || '';
                const nameMatch = cd.match(/filename\*=UTF-8''([^;]+)/);
                const fileName = nameMatch ? decodeURIComponent(nameMatch[1]) : 'shared-file';
                return r.blob().then(function (blob) { return new File([blob], fileName); });
            })
            .then(function (file) { startUpload(file, 'share-target'); })
            .catch(function () { /* expired or gone; stay on the idle screen */ });
    }

    render();
    consumePasteHandoff();
    consumeShareTarget();
})();