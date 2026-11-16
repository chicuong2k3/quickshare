// Recurring delivery schedules (issue #176). One global object, page-scoped.
window.quickShareRecurring = (function () {
    const BODY = '#recurring-body';

    function scheduleUrl(btn: HTMLButtonElement, suffix?: string): string {
        return '/api/recurring/' + qsPage.attr(btn, 'data-id') + (suffix || '');
    }

    return {
        create: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/recurring', 'POST', {
                name: qsPage.value('recurring-name'),
                sourceDeliveryId: qsPage.value('recurring-source'),
                frequency: qsPage.value('recurring-frequency'),
                hourUtc: parseInt(qsPage.value('recurring-hour'), 10) || 0,
                dayOfWeek: parseInt(qsPage.value('recurring-day'), 10) || 1,
                recipients: qsPage.value('recurring-recipients'),
            }, { btn: btn, body: BODY });
        },
        togglePause: function (btn: HTMLButtonElement): void {
            const enabled = qsPage.attr(btn, 'data-enabled') === 'true';
            qsPage.send(scheduleUrl(btn), 'PATCH', { enabled: !enabled }, { btn: btn, body: BODY });
        },
        // Skip is a one-off; pause is indefinite. Two buttons because they are two questions.
        skipNext: function (btn: HTMLButtonElement): void {
            qsPage.send(scheduleUrl(btn), 'PATCH', { skipNext: true }, { btn: btn, body: BODY });
        },
        // No success alert: the refreshed row carries the outcome, including a failure.
        runNow: function (btn: HTMLButtonElement): void {
            qsPage.send(scheduleUrl(btn, '/run'), 'POST', null, { btn: btn, body: BODY });
        },
        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send(scheduleUrl(btn), 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
