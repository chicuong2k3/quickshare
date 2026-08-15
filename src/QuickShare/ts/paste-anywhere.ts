// Ctrl+V anywhere → upload. Loaded on the marketing pages and on /send.
// On /send, upload.js owns the paste event; here we only expose the clipboard helpers.
// Everywhere else, the pasted content is stashed in sessionStorage and handed off to /send.
window.qsPaste = (function () {
    'use strict';

    var HANDOFF_KEY = 'qs.paste';
    var SEND_URL = '/send';
    // sessionStorage caps out around 5MB of characters; a data URL inflates ~4/3.
    var MAX_HANDOFF_CHARS = 4 * 1024 * 1024;

    function stamp(): string { return new Date().toISOString().replace(/[:.]/g, '-'); }

    function isEditable(target: EventTarget | null): boolean {
        if (!target) return false;
        var el = target as HTMLElement;
        var tag = (el.tagName || '').toLowerCase();
        return tag === 'input' || tag === 'textarea' || el.isContentEditable === true;
    }

    // Returns a File built from the clipboard, or null when there is nothing uploadable.
    function fileFromClipboard(event: ClipboardEvent): File | null {
        var data = event.clipboardData;
        if (!data) return null;

        var items = data.items || [];
        for (var i = 0; i < items.length; i++) {
            if (items[i].kind === 'file' && items[i].type && items[i].type.indexOf('image/') === 0) {
                var blob = items[i].getAsFile();
                if (!blob) continue;
                var ext = (items[i].type.split('/')[1] || 'png').split('+')[0];
                return new File([blob], 'pasted-' + stamp() + '.' + ext, { type: items[i].type });
            }
        }

        var text = data.getData('text/plain');
        if (text && text.trim().length > 0)
            return new File([text], 'pasted-' + stamp() + '.txt', { type: 'text/plain' });

        return null;
    }

    function stash(file: File): Promise<boolean> {
        return new Promise<boolean>(function (resolve) {
            var reader = new FileReader();
            reader.onload = function () {
                try {
                    var payload = JSON.stringify({ name: file.name, type: file.type, dataUrl: reader.result });
                    if (payload.length > MAX_HANDOFF_CHARS) {
                        resolve(false);
                        return;
                    }
                    sessionStorage.setItem(HANDOFF_KEY, payload);
                    resolve(true);
                } catch (_) { resolve(false); }
            };
            reader.onerror = function () { resolve(false); };
            reader.readAsDataURL(file);
        });
    }

    // Consumed once by upload.js on /send.
    function takeStashed(): File | null {
        var raw: string | null = null;
        try {
            raw = sessionStorage.getItem(HANDOFF_KEY);
            sessionStorage.removeItem(HANDOFF_KEY);
        } catch (_) { return null; }
        if (!raw) return null;

        try {
            var payload = JSON.parse(raw);
            var parts = payload.dataUrl.split(',');
            var binary = atob(parts[1]);
            var bytes = new Uint8Array(binary.length);
            for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            return new File([bytes], payload.name, { type: payload.type });
        } catch (_) { return null; }
    }

    document.addEventListener('paste', function (event: ClipboardEvent) {
        // /send handles its own paste — no handoff needed there.
        if (window.uploadPage) return;
        if (isEditable(event.target)) return;

        var file = fileFromClipboard(event);
        if (!file) return;
        event.preventDefault();

        stash(file).then(function (ok) {
            // Too big to hand off — send them to /send and let them paste again there.
            window.location.href = ok ? SEND_URL : SEND_URL + '#paste';
        });
    });

    return { fileFromClipboard: fileFromClipboard, isEditable: isEditable, takeStashed: takeStashed };
})();