// Shared page-action helpers for the small config screens (workspace, brand, billing, backup,
// SIEM, DPA, approvals, channel, schedules).
//
// Every one of those pages was doing the same four things — read a field, POST JSON, alert the
// server's error code, swap the HTMX section — in its own copy of the same twenty lines. This is
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
    /** Alerted on success. Omit for actions whose result is visible in the refreshed section. */
    doneLabel?: string | null;
    /** CSS selector of the HTMX section to re-fetch. Omit for a full page reload. */
    body?: string;
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

    function reload(body?: string): void {
        const section = body ? document.querySelector(body) : null;
        if (body && section && window.htmx) {
            window.htmx.ajax('GET', window.location.pathname + window.location.search, {
                target: body,
                select: body,
                swap: 'outerHTML',
            });
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
                    window.alert(body && body.errors ? JSON.stringify(body.errors) : String(response.status));
                });
            }
            if (btn) btn.disabled = false;
            if (opts.doneLabel) window.alert(opts.doneLabel);
            reload(opts.body);
        }).catch(function () {
            if (btn) btn.disabled = false;
        });
    }

    return { value: value, checked: checked, attr: attr, reload: reload, send: send };
})();

window.qsPage = qsPage;
