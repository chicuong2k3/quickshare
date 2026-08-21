// Authenticator enrolment (issue #128). One global object, page-scoped.
//
// The secret and the recovery codes are written into the DOM here rather than server-rendered,
// because both appear exactly once: putting them in the page source would put them in the
// browser cache, the back button and every proxy log between here and the reader.
window.quickShareSecurity = (function () {
    const BODY = '#security-body';

    function show(id: string): void {
        const el = document.getElementById(id);
        if (el) el.hidden = false;
    }

    return {
        start: function (btn: HTMLButtonElement): void {
            btn.disabled = true;
            fetch('/api/account/totp/start', { method: 'POST' })
                .then(function (response) {
                    btn.disabled = false;
                    if (!response.ok) {
                        window.alert(String(response.status));
                        return null;
                    }
                    return response.json();
                })
                .then(function (body: { secret: string; otpAuthUri: string } | null) {
                    if (!body) return;

                    const secret = document.getElementById('security-secret');
                    if (secret) secret.textContent = body.secret;

                    const uri = document.getElementById('security-uri') as HTMLAnchorElement | null;
                    if (uri) uri.href = body.otpAuthUri;

                    btn.hidden = true;
                    show('security-enrolment');
                })
                .catch(function () { btn.disabled = false; });
        },

        confirm: function (btn: HTMLButtonElement): void {
            const code = qsPage.value('security-confirm-code');
            if (!code) return;

            btn.disabled = true;
            fetch('/api/account/totp/confirm', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code: code }),
            })
                .then(function (response) {
                    btn.disabled = false;
                    if (!response.ok) {
                        window.alert(qsPage.attr(btn, 'data-error-label'));
                        return null;
                    }
                    return response.json();
                })
                .then(function (body: { codes: string[] } | null) {
                    if (!body) return;

                    // Rendered before anything else happens: navigating away without saving
                    // these is how somebody locks themselves out of their own account.
                    const list = document.getElementById('security-recovery-list');
                    if (list) {
                        list.textContent = '';
                        body.codes.forEach(function (code) {
                            const item = document.createElement('li');
                            item.textContent = code;
                            list.appendChild(item);
                        });
                    }
                    show('security-recovery');
                    window.alert(qsPage.attr(btn, 'data-saved-label'));
                })
                .catch(function () { btn.disabled = false; });
        },

        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;

            const code = qsPage.value('security-remove-code');
            if (!code) return;

            btn.disabled = true;
            fetch('/api/account/totp', {
                method: 'DELETE',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code: code }),
            }).then(function (response) {
                btn.disabled = false;
                if (response.status === 409) {
                    // A workspace policy requires it. Saying so beats a bare error code.
                    window.alert(qsPage.attr(btn, 'data-locked-label'));
                    return;
                }
                if (!response.ok) {
                    window.alert(String(response.status));
                    return;
                }
                qsPage.reload(BODY);
            }).catch(function () { btn.disabled = false; });
        },
    };
})();
