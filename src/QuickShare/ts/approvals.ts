// Approval queue and workspace policy (issue #162). One global object, page-scoped.
window.quickShareApprovals = (function () {
    const BODY = '#workspace-approvals-body';

    function deliveryUrl(btn: HTMLButtonElement, action: string): string {
        return '/api/deliveries/' + qsPage.attr(btn, 'data-delivery-id') + '/' + action;
    }

    return {
        savePolicy: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/workspace', 'PATCH', {
                requireDeliveryApproval: qsPage.checked('approvals-require'),
                allowSelfApproval: qsPage.checked('approvals-self'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
        approve: function (btn: HTMLButtonElement): void {
            qsPage.send(deliveryUrl(btn, 'approve'), 'POST', null, { btn: btn, body: BODY });
        },
        deny: function (btn: HTMLButtonElement): void {
            // A reason is required by the API, so asking for one here avoids a guaranteed 400.
            const reason = window.prompt(qsPage.attr(btn, 'data-prompt'), '');
            if (!reason) return;
            qsPage.send(deliveryUrl(btn, 'deny'), 'POST', { reason: reason }, { btn: btn, body: BODY });
        },
    };
})();
