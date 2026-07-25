// QuickShareCloud upload page logic — vanilla JS (no Alpine)
// R2 Multipart Upload + Client-Side AES-256-GCM Encryption
(function () {
    'use strict';

    var MAX_FILE_BYTES = 5 * 1024 * 1024 * 1024;
    var PART_TARGET = 25 * 1024 * 1024;
    var CONCURRENT_PARTS = 4;

    var state = {
        phase: 'idle',
        progress: 0, filename: '', link: '', expiresAt: '', error: '',
        expiry: '24h', copied: false, pasteTip: false, draggingActive: false,
        encryptionEnabled: false, _encryptionUsed: false,
        _abortController: null, _deliveryId: null, _uploadId: null,
        fileSize: 0, uploadedBytes: 0, speed: '', eta: '', _startTime: 0
    };

    function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
    function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }

    function render() {
        ['upload-idle','upload-uploading','upload-success','upload-error'].forEach(hide);
        var phaseMap = { idle: 'upload-idle', dragging: 'upload-idle', uploading: 'upload-uploading', success: 'upload-success', error: 'upload-error' };
        var elId = phaseMap[state.phase]; if (elId) show(elId);

        if (state.phase === 'idle' || state.phase === 'dragging') {
            var isDrag = state.draggingActive;
            var idleText = document.getElementById('upload-idle-text');
            var dragText = document.getElementById('upload-drag-text');
            var idleSub = document.getElementById('upload-idle-subtext');
            var dragSub = document.getElementById('upload-drag-subtext');
            var dropIdle = document.getElementById('upload-dropzone-idle');
            var dropDrag = document.getElementById('upload-dropzone-drag');
            if (isDrag) { if (idleText) idleText.classList.add('hidden'); if (dragText) dragText.classList.remove('hidden'); if (idleSub) idleSub.classList.add('hidden'); if (dragSub) dragSub.classList.remove('hidden'); if (dropIdle) dropIdle.classList.add('hidden'); if (dropDrag) dropDrag.classList.remove('hidden'); }
            else { if (idleText) idleText.classList.remove('hidden'); if (dragText) dragText.classList.add('hidden'); if (idleSub) idleSub.classList.remove('hidden'); if (dragSub) dragSub.classList.add('hidden'); if (dropIdle) dropIdle.classList.remove('hidden'); if (dropDrag) dropDrag.classList.add('hidden'); }
            var dz = document.getElementById('upload-dropzone');
            if (dz) { if (state.draggingActive) dz.classList.add('active'); else dz.classList.remove('active'); }
        }
        if (state.phase === 'uploading') {
            document.getElementById('upload-filename').textContent = state.filename;
            document.getElementById('upload-progress-label').textContent = progressLabel();
            
                document.getElementById('upload-progress-bar').setAttribute('aria-valuenow', state.progress);
            if (state.encryptionEnabled) show('upload-encrypt-badge'); else hide('upload-encrypt-badge');
        }
        if (state.phase === 'success') {
            document.getElementById('upload-success-name').textContent = state.filename;
            document.getElementById('share-link').value = state.link;
            if (state.encryptionEnabled || state._encryptionUsed) { show('upload-success-encrypt-badge'); } else hide('upload-success-encrypt-badge');
        }
        if (state.phase === 'error') {
            document.getElementById('upload-error-msg').textContent = state.error || 'An unknown error occurred.';
        }

        // Encrypt toggle UI
        var encBtn = document.getElementById('upload-encrypt-btn');
        var encOff = document.getElementById('upload-encrypt-off');
        var encOn = document.getElementById('upload-encrypt-on');
        if (state.encryptionEnabled) {
            if (encBtn) { encBtn.classList.remove('upload-toggle-btn--off'); encBtn.classList.add('upload-toggle-btn--on'); }
            if (encOff) encOff.classList.add('hidden'); if (encOn) encOn.classList.remove('hidden');
        } else {
            if (encBtn) { encBtn.classList.add('upload-toggle-btn--off'); encBtn.classList.remove('upload-toggle-btn--on'); }
            if (encOn) encOn.classList.add('hidden'); if (encOff) encOff.classList.remove('hidden');
        }

        // Expiry toggle UI
        var btn24 = document.getElementById('upload-expiry-24h');
        var btn7d = document.getElementById('upload-expiry-7d');
        if (state.expiry === '24h') {
            if (btn24) { btn24.classList.remove('upload-expiry-btn--off'); btn24.classList.add('upload-expiry-btn--on'); btn24.setAttribute('aria-checked','true'); }
            if (btn7d) { btn7d.classList.add('upload-expiry-btn--off'); btn7d.classList.remove('upload-expiry-btn--on'); btn7d.setAttribute('aria-checked','false'); }
        } else {
            if (btn7d) { btn7d.classList.remove('upload-expiry-btn--off'); btn7d.classList.add('upload-expiry-btn--on'); btn7d.setAttribute('aria-checked','true'); }
            if (btn24) { btn24.classList.add('upload-expiry-btn--off'); btn24.classList.remove('upload-expiry-btn--on'); btn24.setAttribute('aria-checked','false'); }
        }

        // Paste tip
        if (state.pasteTip) show('upload-paste-tip'); else hide('upload-paste-tip');

        // Expiry label
        var expLabel = document.getElementById('upload-expiry-label');
        if (expLabel && state.expiresAt) {
            try {
                var d = new Date(state.expiresAt);
                if (!isNaN(d.getTime())) expLabel.textContent = 'Expires: ' + d.toLocaleString(undefined, { day:'numeric', month:'short', year:'numeric', hour:'2-digit', minute:'2-digit' });
            } catch(_) {}
        }
    }

    function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }

    function formatBytes(b) { if (b < 1024) return b + ' B'; if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'; if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB'; return (b / 1073741824).toFixed(1) + ' GB'; }

    function progressLabel() {
        if (state.progress === 0) return 'Starting...';
        var l = state.progress + '%';
        if (state.uploadedBytes && state.fileSize) l += ' \u00b7 ' + formatBytes(state.uploadedBytes) + ' / ' + formatBytes(state.fileSize);
        if (state.speed) l += ' \u00b7 ' + state.speed;
        if (state.eta) l += ' \u00b7 ~' + state.eta;
        return l;
    }

    window.uploadPage = {
        toggleEncryption: function () { state.encryptionEnabled = !state.encryptionEnabled; render(); },
        selectExpiry: function (v) { state.expiry = v; render(); },

        onDragOver: function (e) { e.preventDefault(); if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy'; state.draggingActive = true; if (state.phase === 'idle') state.phase = 'dragging'; render(); },
        onDragLeave: function (e) { e.preventDefault(); state.draggingActive = false; if (state.phase === 'dragging') state.phase = 'idle'; render(); },
        onDrop: function (e) { e.preventDefault(); state.draggingActive = false; var files = e.dataTransfer && e.dataTransfer.files; if (files && files.length) { startUpload(files[0]); } else { state.phase = 'idle'; render(); } },

        onFileInput: function (e) { var files = e.target && e.target.files; if (files && files.length) startUpload(files[0]); },
        triggerFileInput: function () { var input = document.querySelector('input[type="file"]'); if (input) input.click(); },

        copyLink: function () {
            if (!state.link) return;
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(state.link).then(function () { state.copied = true; render(); }, function () { copyFallback(); });
            } else { copyFallback(); }
        },

        cancelUpload: function () {
            if (state._abortController) state._abortController.abort();
            if (state._deliveryId && state._uploadId) {
                fetch(apiBase() + '/api/uploads/abort-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ deliveryId: state._deliveryId, uploadId: state._uploadId }) }).catch(function () {});
            }
            reset();
        },

        reset: function () { reset(); render(); }
    };

    function copyFallback() {
        var input = document.getElementById('share-link'); if (!input) return;
        try { input.removeAttribute('readonly'); input.focus(); input.select(); document.execCommand('copy'); state.copied = true; render(); } catch(_) { state.copied = false; } finally { input.setAttribute('readonly', ''); }
    }

    function reset() {
        state.phase = 'idle'; state.progress = 0; state.filename = ''; state.link = ''; state.expiresAt = '';
        state.error = ''; state.copied = false; state.encryptionEnabled = false; state._encryptionUsed = false;
        state._abortController = null; state._deliveryId = null; state._uploadId = null;
        state.uploadedBytes = 0; state.fileSize = 0; state.speed = ''; state.eta = '';
    }

    function startUpload(file) {
        if (!file) return;
        if (file.size > MAX_FILE_BYTES) { state.phase = 'error'; state.error = 'File exceeds the 5 GB upload limit.'; render(); return; }
        state.filename = file.name || 'pasted-image';
        state.fileSize = file.size; state.progress = 0; state.error = ''; state.link = '';
        state.copied = false; state._encryptionUsed = false;
        state.phase = 'uploading'; state._startTime = Date.now(); render();
        doUpload(file).catch(function (e) { if (e.name === 'AbortError') return; state.phase = 'error'; state.error = e.message || 'Upload failed'; render(); });
    }

    async function doUpload(file) {
        var api = apiBase();
        var uploadBlob = file;
        var encryptionKeyStr = null;
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
            uploadBlob = new Blob([combined.buffer]);
            var binary = ''; for (var i = 0; i < rawKey.length; i++) binary += String.fromCharCode(rawKey[i]);
            encryptionKeyStr = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
            state._encryptionUsed = true;
        }

        var effectiveSize = uploadBlob.size;
        var totalParts = effectiveSize <= PART_TARGET ? 1 : Math.ceil(effectiveSize / PART_TARGET);
        var partNumbers = []; for (var p = 1; p <= totalParts; p++) partNumbers.push(p);

        state._abortController = new AbortController(); var signal = state._abortController.signal;

        var startResp = await fetch(api + '/api/uploads/start-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ fileName: file.name, fileSize: originalSize, contentType: file.type || 'application/octet-stream', partNumbers: partNumbers }) });
        if (!startResp.ok) { var errT = await startResp.text(); throw new Error(errT || 'Failed to start upload'); }
        var session = await startResp.json();
        state._deliveryId = session.deliveryId; state._uploadId = session.uploadId; state.objectKey = session.objectKey;
        var partList = session.parts;

        var completedParts = 0; var partEtags = [];

        function uploadOnePart(partInfo, idx) {
            var partSize = Math.ceil(effectiveSize / totalParts);
            var start = idx * partSize; var end = Math.min(start + partSize, effectiveSize);
            return fetch(partInfo.url, { method: 'PUT', body: uploadBlob.slice(start, end), signal: signal }).then(function (resp) {
                if (!resp.ok) return resp.text().then(function (t) { throw new Error('Part ' + partInfo.partNumber + ' failed: ' + t); });
                var etag = resp.headers.get('ETag') || ''; etag = etag.replace(/^"(.*)"$/, '$1');
                partEtags[idx] = { partNumber: partInfo.partNumber, etag: etag };
                completedParts++; state.progress = Math.round((completedParts / totalParts) * 100);
                var elapsed = (Date.now() - state._startTime) / 1000;
                if (elapsed > 0) { var doneFrac = completedParts / totalParts; var uploadedSoFar = doneFrac * effectiveSize; var bps = uploadedSoFar / elapsed; state.speed = formatBytes(bps) + '/s'; var remaining = (effectiveSize - uploadedSoFar) / bps; state.eta = remaining < 60 ? Math.round(remaining) + 's' : Math.round(remaining / 60) + 'm'; }
                render();
            });
        }

        for (var batchStart = 0; batchStart < totalParts; batchStart += CONCURRENT_PARTS) {
            var batchEnd = Math.min(batchStart + CONCURRENT_PARTS, totalParts);
            var batch = []; for (var bp = batchStart; bp < batchEnd; bp++) batch.push(uploadOnePart(partList[bp], bp));
            var results = await Promise.allSettled(batch);
            for (var r = 0; r < results.length; r++) { if (results[r].status === 'rejected') throw results[r].reason; }
        }

        var commitResp = await fetch(api + '/api/uploads/commit-r2', { method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: signal, body: JSON.stringify({ deliveryId: state._deliveryId, uploadId: state._uploadId, objectKey: state.objectKey, fileName: file.name, contentType: file.type || 'application/octet-stream', fileSize: originalSize, parts: partEtags }) });
        if (!commitResp.ok) { var cErr = await commitResp.text(); throw new Error(cErr || 'Failed to finalize upload'); }
        var result = await commitResp.json();
        state.link = result.shareLink;
        if (encryptionKeyStr) state.link += '#key=' + encryptionKeyStr;
        if (result.expiresAt) { state.expiresAt = result.expiresAt; }
        state.phase = 'success'; render();
        renderQR(state.link);
        window.uploadPage.copyLink();
    }

    function renderQR(text) {
        var el = document.getElementById('qrcode'); if (!el) return; el.innerHTML = '';
        if (!text || !window.qrcode) { if (!window.qrcode) el.textContent = 'QR library not loaded.'; return; }
        try {
            var qr = window.qrcode(0, 'M'); qr.addData(text); qr.make();
            var moduleCount = qr.getModuleCount(); var cell = 6; var dim = moduleCount * cell;
            var parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="'+dim+'" height="'+dim+'" viewBox="0 0 '+dim+' '+dim+'" role="img" aria-label="QR code" style="image-rendering:pixelated"><rect width="'+dim+'" height="'+dim+'" fill="#fff"/>'];
            for (var r = 0; r < moduleCount; r++) for (var c = 0; c < moduleCount; c++) if (qr.isDark(r, c)) parts.push('<rect x="'+(c*cell)+'" y="'+(r*cell)+'" width="'+cell+'" height="'+cell+'" fill="#0f172a"/>');
            parts.push('</svg>'); el.innerHTML = parts.join('');
        } catch(e) { el.textContent = 'QR unavailable.'; }
    }

    document.addEventListener('paste', function (e) {
        if (state.phase === 'uploading') return;
        var items = e.clipboardData && e.clipboardData.items; if (!items) return;
        for (var i = 0; i < items.length; i++) { if (items[i].kind === 'file' && items[i].type && items[i].type.indexOf('image/') === 0) { e.preventDefault(); startUpload(items[i].getAsFile()); return; } }
        state.pasteTip = true; render(); setTimeout(function () { state.pasteTip = false; render(); }, 2000);
    });

    render();
})();
