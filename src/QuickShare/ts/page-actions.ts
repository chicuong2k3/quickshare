// Shared page-action helpers for the small config screens (workspace, brand, billing, backup,
// SIEM, DPA, approvals, channel, schedules).
//
// Every one of those pages was doing the same four things — read a field, POST JSON, alert the
// server's error code, refresh the section — in its own copy of the same twenty lines. This is
// that copy, once. It is loaded globally from App.razor because a page script that needs it must
// not have to remember to include it.
//
// The shape it enforces: the server is the authority. On failure we surface the code the server
// sent rather than inventing a friendlier message it did not; on success we re-fetch the section
// rather than patching the DOM, because a half-updated card is how a revoked member appears to
// still hold a seat.

interface QsSendOptions {
    /** Disabled for the duration and re-enabled on failure. */
    btn?: HTMLButtonElement | null;
    /** Shown as a toast on success. Omit for actions whose result is visible in the refreshed section. */
    doneLabel?: string | null;
    /** CSS selector of the section to re-fetch. Omit for a full page reload. */
    body?: string;
}

/** Labels for the two answers. Defaults come from window.shadcnUi (App.razor), localized. */
interface QsAskOptions {
    confirmText?: string;
    cancelText?: string;
}

const qsPage = (function () {
    function element(id: string): HTMLInputElement | null {
        return document.getElementById(id) as HTMLInputElement | null;
    }

    function value(id: string): string {
        const el = element(id);
        return el ? el.value.trim() : '';
    }

    function checked(id: string): boolean {
        const el = element(id);
        return !!(el && el.checked);
    }

    /** getAttribute without the null: a missing data- attribute reads as absent, not as a crash. */
    function attr(el: Element | null, name: string): string {
        const raw = el ? el.getAttribute(name) : null;
        return raw === null ? '' : raw;
    }

    /**
     * Asks a destructive question in the library's AlertDialog and resolves with the answer.
     *
     * window.confirm cannot be used here: it blocks the whole tab, renders in the browser's own
     * chrome instead of the board, and a browser is free to suppress it after the first few — a
     * delete that silently goes ahead because the prompt never appeared is the worst outcome
     * available. Escaping or clicking the backdrop answers no.
     */
    function ask(message: string, options?: QsAskOptions): Promise<boolean> {
        if (!message) return Promise.resolve(true);
        return window.alertDialog(message, options);
    }

    /** Replaces window.alert: news, not a roadblock. Errors default to the assertive live region. */
    function notify(message: string, type?: string): void {
        if (!message) return;
        window.toast(message, { type: type || 'error' });
    }

    function reload(body?: string): void {
        const section = body ? document.querySelector(body) : null;
        const unpoly = (window as Window & {
            up?: { reload: (selector: string, options: { cache: boolean }) => Promise<unknown> };
        }).up;
        if (body && section && unpoly) {
            void unpoly.reload(body, { cache: false }).catch(() => window.location.reload());
            return;
        }
        window.location.reload();
    }

    function send(url: string, method: string, payload: unknown, options?: QsSendOptions): Promise<void> {
        const opts = options || {};
        const btn = opts.btn || null;
        if (btn) btn.disabled = true;

        return fetch(url, {
            method: method,
            headers: { 'Content-Type': 'application/json' },
            body: payload === null || payload === undefined ? undefined : JSON.stringify(payload),
        }).then(function (response) {
            if (!response.ok) {
                if (btn) btn.disabled = false;
                return response.json().catch(function () { return null; }).then(function (body: any) {
                    notify(body && body.errors ? JSON.stringify(body.errors) : String(response.status));
                });
            }
            if (btn) btn.disabled = false;
            if (opts.doneLabel) notify(opts.doneLabel, 'success');
            reload(opts.body);
        }).catch(function () {
            if (btn) btn.disabled = false;
        });
    }

    return { value: value, checked: checked, attr: attr, reload: reload, send: send, ask: ask, notify: notify };
})();

window.qsPage = qsPage;
