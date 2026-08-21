// The second-factor challenge (issue #128). One global object, page-scoped.
window.quickShareMfa = (function () {
    return {
        verify: function (btn: HTMLButtonElement): void {
            const code = qsPage.value('mfa-code');
            if (!code) return;

            btn.disabled = true;
            fetch('/api/account/totp/verify', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code: code }),
            }).then(function (response) {
                btn.disabled = false;
                if (!response.ok) {
                    window.alert(qsPage.attr(btn, 'data-error-label'));
                    return;
                }
                // Full navigation, not a section swap: the cookie was just re-issued with the
                // verified claim, and the point is to leave this page for good.
                window.location.href = '/';
            }).catch(function () { btn.disabled = false; });
        },
    };
})();
