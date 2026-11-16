// Billing details form (issues #112 VAT fields, #192 PO + net terms). One global object,
// page-scoped, per the project JS convention.
window.quickShareBilling = (function () {
    const BODY = '#invoices-body';

    return {
        saveProfile: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/billing/profile', 'PUT', {
                companyName: qsPage.value('bill-company'),
                taxCode: qsPage.value('bill-taxcode'),
                address: qsPage.value('bill-address'),
                billingEmail: qsPage.value('bill-email'),
                poNumber: qsPage.value('bill-po'),
                netTermsDays: parseInt(qsPage.value('bill-terms'), 10) || 0,
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-saved-label'), body: BODY });
        },
    };
})();
