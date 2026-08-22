// White-label custom domain (issue #81). One global object, page-scoped.
window.quickShareDomain = (function () {
    const BODY = '#workspace-domain-body';
    const URL = '/api/workspace/domain';

    return {
        save: function (btn: HTMLButtonElement): void {
            qsPage.send(URL, 'PUT', { host: qsPage.value('domain-host') },
                { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        // No done label: the refreshed status card is the answer, and DNS says no more often
        // than it says yes on the first attempt.
        verify: function (btn: HTMLButtonElement): void {
            qsPage.send(URL + '/verify', 'POST', null, { btn: btn, body: BODY });
        },
        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send(URL, 'DELETE', null, { btn: btn, body: BODY });
        },
    };
})();
