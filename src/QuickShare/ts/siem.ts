// Audit-log streaming configuration (issue #191). One global object, page-scoped.
window.quickShareSiem = (function () {
    const BODY = '#workspace-streaming-body';
    const URL = '/api/workspace/streaming';

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send(URL, 'PUT', {
                endpoint: qsPage.value('siem-endpoint'),
                format: qsPage.value('siem-format'),
                // Blank leaves the stored secret alone — the field is never populated back.
                secret: qsPage.value('siem-secret'),
                eventFilter: qsPage.value('siem-filter'),
                enabled: qsPage.checked('siem-enabled'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send(URL, 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
