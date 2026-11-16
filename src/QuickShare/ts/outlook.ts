// Outlook add-in task pane (issue #194). Runs inside Outlook's webview, not in our app shell.
//
// The whole feature is one gesture: the mail has a 25 MB attachment, the button turns it into a
// QuickShare link and takes the attachment off the message. Everything else — expiry, password,
// branding — is the account's existing defaults, because a task pane the width of a phone is the
// wrong place to re-litigate settings that already have a home.
//
// It authenticates with an API key (#115). The webview is a third-party context and our cookie
// does not reliably travel into it; a key is pasted once, kept in this webview's localStorage,
// and revocable from the developer screen like any other.

interface OutlookStrings {
    needKey: string;
    saved: string;
    noAttachments: string;
    uploading: string;
    inserted: string;
    failed: string;
}

window.quickShareOutlook = (function () {
    const KEY_STORAGE = 'qs-outlook-key';
    const PART_SIZE = 5 * 1024 * 1024;
    const STATUS = 'outlook-status';

    function strings(): OutlookStrings {
        const el = document.getElementById('outlook-i18n');
        return el ? JSON.parse(el.textContent || '{}') : ({} as OutlookStrings);
    }

    function say(message: string): void {
        const status = document.getElementById(STATUS);
        if (status) status.textContent = message;
    }

    function apiKey(): string {
        return window.localStorage.getItem(KEY_STORAGE) || '';
    }

    function headers(json: boolean): Record<string, string> {
        const result: Record<string, string> = { 'X-Api-Key': apiKey() };
        if (json) result['Content-Type'] = 'application/json';
        return result;
    }

    /** Office hands attachment bytes over as base64; R2 wants a Blob. */
    function toBlob(base64: string, contentType: string): Blob {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return new Blob([bytes], { type: contentType });
    }

    function item(): any {
        return (window as any).Office?.context?.mailbox?.item;
    }

    function attachmentsAsync(): Promise<any[]> {
        return new Promise(function (resolve) {
            const current = item();
            if (!current || !current.getAttachmentsAsync) {
                resolve([]);
                return;
            }
            current.getAttachmentsAsync(function (result: any) {
                resolve(result.status === 'succeeded' && result.value ? result.value : []);
            });
        });
    }

    function contentAsync(attachmentId: string): Promise<string | null> {
        return new Promise(function (resolve) {
            item().getAttachmentContentAsync(attachmentId, function (result: any) {
                resolve(result.status === 'succeeded' && result.value ? result.value.content : null);
            });
        });
    }

    function removeAsync(attachmentId: string): Promise<void> {
        return new Promise(function (resolve) {
            item().removeAttachmentAsync(attachmentId, function () { resolve(); });
        });
    }

    function insertAsync(html: string): Promise<void> {
        return new Promise(function (resolve) {
            // Prepended rather than replacing the selection: the writer is mid-sentence
            // somewhere, and dropping a link into the middle of it is a worse surprise than one
            // extra line at the top they can move.
            item().body.prependAsync(html, { coercionType: 'html' }, function () { resolve(); });
        });
    }

    /** start-r2 → PUT each presigned part → commit. The same three steps the web upload takes. */
    async function upload(name: string, blob: Blob): Promise<string> {
        const totalParts = Math.max(1, Math.ceil(blob.size / PART_SIZE));
        const partNumbers: number[] = [];
        for (let p = 1; p <= totalParts; p++) partNumbers.push(p);

        const startResponse = await fetch('/api/uploads/start-r2', {
            method: 'POST',
            headers: headers(true),
            body: JSON.stringify({
                fileName: name,
                fileSize: blob.size,
                contentType: blob.type || 'application/octet-stream',
                partNumbers: partNumbers,
            }),
        });
        if (!startResponse.ok) throw new Error(String(startResponse.status));
        const start = await startResponse.json();

        const etags: { partNumber: number; etag: string }[] = [];
        for (let i = 0; i < start.parts.length; i++) {
            const part = start.parts[i];
            const from = (part.partNumber - 1) * PART_SIZE;
            const put = await fetch(part.url, { method: 'PUT', body: blob.slice(from, Math.min(from + PART_SIZE, blob.size)) });
            if (!put.ok) throw new Error(String(put.status));
            etags.push({
                partNumber: part.partNumber,
                etag: (put.headers.get('ETag') || '').replace(/^"(.*)"$/, '$1'),
            });
        }

        const commit = await fetch('/api/uploads/commit-r2', {
            method: 'POST',
            headers: headers(true),
            body: JSON.stringify({
                deliveryId: start.deliveryId,
                uploadId: start.uploadId,
                objectKey: start.objectKey,
                fileName: name,
                contentType: blob.type || 'application/octet-stream',
                fileSize: blob.size,
                parts: etags,
                source: 'outlook',
            }),
        });
        if (!commit.ok) throw new Error(String(commit.status));

        return (await commit.json()).shareLink;
    }

    return {
        saveKey: function (): void {
            const field = document.getElementById('outlook-key') as HTMLInputElement | null;
            const value = field ? field.value.trim() : '';
            if (!value) return;

            window.localStorage.setItem(KEY_STORAGE, value);
            if (field) field.value = '';
            say(strings().saved);
        },

        share: async function (button: HTMLButtonElement): Promise<void> {
            const text = strings();
            if (!apiKey()) {
                say(text.needKey);
                return;
            }

            button.disabled = true;
            try {
                const attachments = await attachmentsAsync();
                if (attachments.length === 0) {
                    say(text.noAttachments);
                    return;
                }

                const links: string[] = [];
                for (let i = 0; i < attachments.length; i++) {
                    const attachment = attachments[i];
                    say(text.uploading.replace('{0}', attachment.name));

                    const content = await contentAsync(attachment.id);
                    if (!content) continue;

                    const link = await upload(attachment.name, toBlob(content, attachment.contentType || ''));
                    links.push('<div><a href="' + link + '">' + attachment.name + '</a></div>');

                    // Only once the link exists: removing first and failing after would lose the
                    // file from a mail nobody has sent yet.
                    await removeAsync(attachment.id);
                }

                if (links.length === 0) {
                    say(text.noAttachments);
                    return;
                }

                await insertAsync(links.join(''));
                say(text.inserted.replace('{0}', String(links.length)));
            } catch (error) {
                say(text.failed.replace('{0}', String(error)));
            } finally {
                button.disabled = false;
            }
        },
    };
})();

// Office calls back when the host is ready; nothing here touches the mailbox before that.
(function () {
    const office = (window as any).Office;
    if (office && office.onReady) {
        office.onReady(function () {
            const shell = document.getElementById('outlook-shell');
            if (shell) shell.hidden = false;
        });
    }
})();
