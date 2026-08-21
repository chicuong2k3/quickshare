// Slack/Teams channel configuration (issue #180). One global object, page-scoped.
window.quickShareChannel = (function () {
    const BODY = '#workspace-channel-body';
    const URL = '/api/workspace/channel';

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send(URL, 'PUT', {
                kind: qsPage.value('channel-kind'),
                // Blank keeps the stored webhook — it is never populated back into the field.
                webhookUrl: qsPage.value('channel-url'),
                enabled: qsPage.checked('channel-enabled'),
                notifyOnDelivery: qsPage.checked('channel-on-delivery'),
                notifyOnComment: qsPage.checked('channel-on-comment'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send(URL, 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
