// Scheduled workspace backup (issue #271). One global object, page-scoped.
window.quickShareBackup = (function () {
    const BODY = '#workspace-backup-body';
    const URL = '/api/workspace/backup';

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send(URL, 'PUT', {
                enabled: qsPage.checked('backup-enabled'),
                frequency: qsPage.value('backup-frequency'),
                retainedCopies: parseInt(qsPage.value('backup-retained'), 10) || 1,
                endpoint: qsPage.value('backup-endpoint'),
                region: qsPage.value('backup-region'),
                bucketName: qsPage.value('backup-bucket'),
                keyPrefix: qsPage.value('backup-prefix'),
                accessKeyId: qsPage.value('backup-key'),
                // Blank leaves the stored secret alone — never populated back into the form.
                accessKeySecret: qsPage.value('backup-secret'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        // Reloads without an alert: the run outcome, success or failure, is in the panel the
        // reload renders. A popup saying "done" over a failed run would be a lie.
        runNow: function (btn: HTMLButtonElement): void {
            qsPage.send(URL + '/run', 'POST', null, { btn: btn, body: BODY });
        },
        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send(URL, 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
