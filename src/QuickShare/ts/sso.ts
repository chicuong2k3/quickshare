// Enterprise SSO binding (#181), MFA policy (#219) and SCIM tokens (#146). One global object.
window.quickShareSso = (function () {
    const BODY = '#workspace-sso-body';

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/workspace/sso', 'PUT', {
                provider: qsPage.value('sso-provider'),
                directoryId: qsPage.value('sso-directory'),
                allowedEmailDomains: qsPage.value('sso-domains'),
                jitRole: qsPage.value('sso-jit-role'),
                adminGroupId: qsPage.value('sso-admin-group'),
                enabled: qsPage.checked('sso-enabled'),
                requireSso: qsPage.checked('sso-require'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },

        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send('/api/workspace/sso', 'DELETE', null, { btn: btn, body: BODY });
        },

        savePolicy: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/workspace/mfa', 'PUT', {
                mfaRequired: qsPage.checked('mfa-required'),
                mfaGraceDays: parseInt(qsPage.value('mfa-grace'), 10) || 0,
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },

        // Not qsPage.send: the response carries the one and only copy of the raw token, so the
        // section must not be swapped out from under it before it has been shown.
        createToken: function (btn: HTMLButtonElement): void {
            btn.disabled = true;
            fetch('/api/workspace/scim-tokens', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ label: qsPage.value('scim-label') }),
            })
                .then(function (response) {
                    btn.disabled = false;
                    if (!response.ok) {
                        window.alert(String(response.status));
                        return null;
                    }
                    return response.json();
                })
                .then(function (body: { token: string } | null) {
                    if (!body) return;

                    const value = document.getElementById('scim-new-value');
                    if (value) value.textContent = body.token;

                    const holder = document.getElementById('scim-new');
                    if (holder) holder.hidden = false;

                    window.alert(qsPage.attr(btn, 'data-once-label'));
                })
                .catch(function () { btn.disabled = false; });
        },

        revokeToken: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send('/api/workspace/scim-tokens/' + qsPage.attr(btn, 'data-id'), 'DELETE', null,
                { btn: btn, body: BODY });
        },
    };
})();
