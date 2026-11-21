// API keys (#115) and webhooks (#39, #172, #223, #288). One global object, page-scoped.
window.quickShareDeveloper = (function () {
    const BODY = '#developer-body';

    /**
     * Reveals a value the server will never send again.
     *
     * Both the API key and the webhook secret exist in exactly one response, so this deliberately
     * does not refresh the section afterwards: swapping the DOM would take the only copy away
     * before it had been read.
     */
    function revealOnce(btn: HTMLButtonElement, holderId: string, valueId: string, value: string): void {
        const target = document.getElementById(valueId);
        if (target) target.textContent = value;

        const holder = document.getElementById(holderId);
        if (holder) holder.hidden = false;

        // A toast, not a dialog: the secret is already on screen and this line only says so.
        qsPage.notify(qsPage.attr(btn, 'data-once-label'), 'success');
    }

    function postJson(url: string, payload: unknown, btn: HTMLButtonElement): Promise<any | null> {
        btn.disabled = true;
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: payload ? JSON.stringify(payload) : undefined,
        }).then(function (response) {
            btn.disabled = false;
            if (!response.ok) {
                qsPage.notify(String(response.status));
                return null;
            }
            return response.status === 204 ? {} : response.json();
        }).catch(function () {
            btn.disabled = false;
            return null;
        });
    }

    return {
        createKey: function (btn: HTMLButtonElement): void {
            postJson('/api/developer/keys', {
                name: qsPage.value('dev-key-name'),
                scopes: qsPage.value('dev-key-scopes'),
            }, btn).then(function (body: { key: string } | null) {
                if (body && body.key) revealOnce(btn, 'dev-key-new', 'dev-key-new-value', body.key);
            });
        },

        // S3 gateway credentials (#265). The secret is shown once, like every other secret here.
        createS3: function (btn: HTMLButtonElement): void {
            postJson('/api/workspace/s3-credentials', {
                label: qsPage.value('dev-s3-label'),
                scopes: qsPage.value('dev-s3-scopes'),
            }, btn).then(function (body: { secret: string; accessKeyId: string } | null) {
                if (body && body.secret) {
                    revealOnce(btn, 'dev-s3-new', 'dev-s3-new-value', body.accessKeyId + ' / ' + body.secret);
                }
            });
        },

        revokeS3: async function (btn: HTMLButtonElement): Promise<void> {
            if (!(await qsPage.ask(qsPage.attr(btn, 'data-confirm')))) return;
            qsPage.send('/api/workspace/s3-credentials/' + qsPage.attr(btn, 'data-id'), 'DELETE', null,
                { btn: btn, body: BODY });
        },

        // Per-key usage (issue #273): 30 daily buckets, summed and listed as text. A sparkline
        // would need a chart library for a number people read once a week.
        usage: function (btn: HTMLButtonElement): void {
            const holder = document.getElementById(qsPage.attr(btn, 'data-target'));
            if (!holder) return;
            if (holder.textContent) { holder.textContent = ''; return; }

            fetch('/api/developer/keys/' + qsPage.attr(btn, 'data-id') + '/usage')
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (days: { day: string; requests: number }[] | null) {
                    if (!days || days.length === 0) {
                        holder.textContent = qsPage.attr(btn, 'data-empty');
                        return;
                    }
                    let total = 0;
                    for (let i = 0; i < days.length; i++) total += days[i].requests;
                    holder.textContent = qsPage.attr(btn, 'data-total').replace('{0}', String(total)) +
                        ' \u00b7 ' + days.map(function (d) { return d.day + ': ' + d.requests; }).join(', ');
                })
                .catch(function () { holder.textContent = qsPage.attr(btn, 'data-empty'); });
        },

        revokeKey: async function (btn: HTMLButtonElement): Promise<void> {
            if (!(await qsPage.ask(qsPage.attr(btn, 'data-confirm')))) return;
            qsPage.send('/api/developer/keys/' + qsPage.attr(btn, 'data-id'), 'DELETE', null,
                { btn: btn, body: BODY });
        },

        createHook: function (btn: HTMLButtonElement): void {
            postJson('/api/developer/webhooks', {
                url: qsPage.value('dev-hook-url'),
                events: qsPage.value('dev-hook-events'),
                enabled: true,
            }, btn).then(function (body: { secret: string } | null) {
                if (body && body.secret) revealOnce(btn, 'dev-hook-new', 'dev-hook-new-value', body.secret);
            });
        },

        // Saving also clears a parked failure count, so this doubles as "try again".
        toggleHook: function (btn: HTMLButtonElement): void {
            const enabled = qsPage.attr(btn, 'data-enabled') === 'true';
            qsPage.send('/api/developer/webhooks/' + qsPage.attr(btn, 'data-id'), 'PATCH',
                { enabled: !enabled }, { btn: btn, body: BODY });
        },

        deleteHook: async function (btn: HTMLButtonElement): Promise<void> {
            if (!(await qsPage.ask(qsPage.attr(btn, 'data-confirm')))) return;
            qsPage.send('/api/developer/webhooks/' + qsPage.attr(btn, 'data-id'), 'DELETE', null,
                { btn: btn, body: BODY });
        },

        // The queue picks it up within the minute; refreshing shows it land in the log below.
        testHook: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/developer/webhooks/' + qsPage.attr(btn, 'data-id') + '/test', 'POST', null,
                { btn: btn, body: BODY });
        },

        replay: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/developer/webhooks/deliveries/' + qsPage.attr(btn, 'data-id') + '/replay',
                'POST', null, { btn: btn, body: BODY });
        },
    };
})();
