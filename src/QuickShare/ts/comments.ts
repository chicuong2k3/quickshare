// Delivery comment thread (issue #289) on the recipient page.
//
// The thread itself is server-rendered, so a reader with scripts off still sees the
// conversation. This script only adds posting and replying, and the form it drives is marked
// .dl-comments__js so a <noscript> rule can hide it when scripting is unavailable.
window.quickShareComments = (function () {
    let parentId: string | null = null;

    function shareCode(): string {
        return qsPage.attr(document.getElementById('dl-comments'), 'data-share-code');
    }

    function setReplying(on: boolean): void {
        const hint = document.getElementById('dl-comment-replying');
        if (hint) hint.hidden = !on;
    }

    return {
        reply: function (btn: HTMLButtonElement): void {
            parentId = qsPage.attr(btn, 'data-parent-id');
            setReplying(true);
            const body = document.getElementById('dl-comment-body');
            if (body) body.focus();
        },
        cancelReply: function (): void {
            parentId = null;
            setReplying(false);
        },
        post: function (btn: HTMLButtonElement): void {
            const code = shareCode();
            const body = qsPage.value('dl-comment-body');
            if (!code || !body) return;

            // Reload rather than splice: the server decides ordering, tombstones and which
            // authors count as team members, and re-deriving that here would be a second
            // implementation to keep in step.
            qsPage.send('/api/deliveries/' + encodeURIComponent(code) + '/comments', 'POST', {
                body: body,
                parentId: parentId,
                authorName: qsPage.value('dl-comment-name') || null,
            }, { btn: btn });
        },
    };
})();
