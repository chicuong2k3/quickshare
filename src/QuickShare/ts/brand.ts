// Workspace branding (issues #285 logo/colours, #286 fonts). One global object, page-scoped.
window.quickShareBrand = (function () {
    const BODY = '#workspace-brand-body';

    function assetUrl(btn: HTMLButtonElement): string {
        return '/api/workspace/brand/assets/' + qsPage.attr(btn, 'data-kind');
    }

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/workspace/brand', 'PUT', {
                enabled: qsPage.checked('brand-enabled'),
                accentColor: qsPage.value('brand-accent'),
                accentTextColor: qsPage.value('brand-accent-text'),
                headerText: qsPage.value('brand-header'),
                footerText: qsPage.value('brand-footer'),
                hideQuickShareBranding: qsPage.checked('brand-hide-qs'),
                headingFontName: qsPage.value('brand-heading-name'),
                bodyFontName: qsPage.value('brand-body-name'),
                fontLicenseNote: qsPage.value('brand-license'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        // Not qsPage.send: this posts multipart, not JSON. The colour, contrast and file-type
        // rules stay server-side either way.
        upload: function (btn: HTMLButtonElement): void {
            const input = document.getElementById(qsPage.attr(btn, 'data-input')) as HTMLInputElement | null;
            if (!input || !input.files || input.files.length === 0) return;

            const form = new FormData();
            form.append('file', input.files[0]);

            btn.disabled = true;
            fetch(assetUrl(btn), { method: 'POST', body: form }).then(function (response) {
                btn.disabled = false;
                if (!response.ok) {
                    window.alert(String(response.status));
                    return;
                }
                qsPage.reload(BODY);
            }).catch(function () { btn.disabled = false; });
        },
        removeAsset: function (btn: HTMLButtonElement): void {
            qsPage.send(assetUrl(btn), 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
