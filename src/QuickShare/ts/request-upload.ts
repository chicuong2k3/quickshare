// Anonymous submission to a file request or drop room (issues #16, #17).
//
// The blocked half of #16: the owner-side of file requests shipped batches ago, and this page
// never existed because the encryption question was open. It is answered here and on the page
// itself — a drop from a stranger is *not* end-to-end encrypted, because E2E works by keeping
// the key in the URL fragment and there is nobody to hand that key to. The owner has to be able
// to open what was dropped.
//
// The upload is the same three steps the send page takes: start, PUT each presigned part,
// commit. Nothing here is request-specific except the code that says which request it is for.

window.requestUpload = (function () {
    const PART_SIZE = 5 * 1024 * 1024;

    function say(message: string): void {
        const status = document.getElementById('request-status');
        if (status) status.textContent = message;
    }

    async function upload(code: string, file: File): Promise<void> {
        const totalParts = Math.max(1, Math.ceil(file.size / PART_SIZE));
        const partNumbers: number[] = [];
        for (let p = 1; p <= totalParts; p++) partNumbers.push(p);

        const startResponse = await fetch('/api/uploads/start-r2', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                fileName: file.name,
                fileSize: file.size,
                contentType: file.type || 'application/octet-stream',
                partNumbers: partNumbers,
                // What makes this an anonymous submission rather than an upload: the server
                // checks the request's own budget and expiry against it.
                requestCode: code,
            }),
        });
        if (!startResponse.ok) throw new Error(String(startResponse.status));
        const start = await startResponse.json();

        const etags: { partNumber: number; etag: string }[] = [];
        for (let i = 0; i < start.parts.length; i++) {
            const part = start.parts[i];
            const from = (part.partNumber - 1) * PART_SIZE;
            const put = await fetch(part.url, {
                method: 'PUT',
                body: file.slice(from, Math.min(from + PART_SIZE, file.size)),
            });
            if (!put.ok) throw new Error(String(put.status));
            etags.push({
                partNumber: part.partNumber,
                etag: (put.headers.get('ETag') || '').replace(/^"(.*)"$/, '$1'),
            });
        }

        const commit = await fetch('/api/uploads/commit-r2', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                deliveryId: start.deliveryId,
                uploadId: start.uploadId,
                objectKey: start.objectKey,
                fileName: file.name,
                contentType: file.type || 'application/octet-stream',
                fileSize: file.size,
                parts: etags,
            }),
        });
        if (!commit.ok) throw new Error(String(commit.status));
    }

    return {
        send: async function (button: HTMLButtonElement): Promise<void> {
            const input = document.getElementById('request-file') as HTMLInputElement | null;
            const files = input && input.files ? Array.from(input.files) : [];
            if (files.length === 0) return;

            const code = button.getAttribute('data-code') || '';
            button.disabled = true;

            try {
                for (let i = 0; i < files.length; i++) {
                    say((button.getAttribute('data-uploading') || '').replace('{0}', files[i].name));
                    await upload(code, files[i]);
                }

                say(button.getAttribute('data-done') || '');
                // Reloaded rather than patched: in a room the new file belongs in the list
                // everybody sees, and re-rendering server-side is one line instead of a
                // client-side copy of that list.
                window.setTimeout(function () { window.location.reload(); }, 1200);
            } catch (error) {
                say((button.getAttribute('data-failed') || '').replace('{0}', String(error)));
                button.disabled = false;
            }
        },
    };
})();
