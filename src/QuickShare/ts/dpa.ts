// DPA signing (issue #184). One global object, page-scoped, per the project JS convention.
window.quickShareDpa = (function () {
    return {
        // Full reload rather than a section swap: the page becomes the signed record, and half
        // of it updating would leave the terms and the evidence disagreeing on screen.
        sign: function (btn: HTMLButtonElement): void {
            qsPage.send('/api/dpa/sign', 'POST', {
                companyName: qsPage.value('dpa-company'),
                signerName: qsPage.value('dpa-signer'),
                signerEmail: qsPage.value('dpa-email'),
            }, { btn: btn, doneLabel: qsPage.attr(btn, 'data-signed-label') });
        },
    };
})();
