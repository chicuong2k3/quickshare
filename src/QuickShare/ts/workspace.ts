// Team workspace controls (issues #106, #252). One global object, page-scoped. Every call
// reloads the section on success rather than patching the DOM by hand — seat counts, roles and
// pending invites all move together, and a half-updated card is how a revoked member appears to
// still hold a seat.
window.quickShareWorkspace = (function () {
    const BODY = '#workspace-body';

    function send(url: string, method: string, payload: unknown, btn?: HTMLButtonElement, doneLabel?: string): void {
        qsPage.send(url, method, payload, { btn: btn, doneLabel: doneLabel, body: BODY });
    }

    function folderUrl(btn: HTMLButtonElement): string {
        return '/api/workspace/folders/' + qsPage.attr(btn, 'data-folder-id');
    }

    function templateUrl(btn: HTMLButtonElement, suffix?: string): string {
        return '/api/workspace/templates/' + qsPage.attr(btn, 'data-template-id') + (suffix || '');
    }

    return {
        create: function (): void {
            send('/api/workspace', 'POST', {
                name: qsPage.value('ws-name'),
                seats: parseInt(qsPage.value('ws-seats'), 10),
            });
        },
        invite: function (btn: HTMLButtonElement): void {
            send('/api/workspace/invites', 'POST', {
                email: qsPage.value('ws-invite-email'),
                role: qsPage.value('ws-invite-role'),
            }, btn, qsPage.attr(btn, 'data-sent-label'));
        },
        revoke: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            send('/api/workspace/members/' + qsPage.attr(btn, 'data-member-id'), 'DELETE', null, btn);
        },
        transfer: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            send('/api/workspace/transfer', 'POST', { memberId: qsPage.attr(btn, 'data-member-id') }, btn);
        },
        cancelTransfer: function (btn: HTMLButtonElement): void {
            send('/api/workspace/transfer/cancel', 'POST', null, btn);
        },
        createFolder: function (btn: HTMLButtonElement): void {
            const parent = qsPage.value('ws-folder-parent');
            send('/api/workspace/folders', 'POST', {
                name: qsPage.value('ws-folder-name'),
                parentId: parent || null,
            }, btn);
        },
        renameFolder: function (btn: HTMLButtonElement): void {
            const next = window.prompt(qsPage.attr(btn, 'data-prompt'), qsPage.attr(btn, 'data-current'));
            if (!next) return;
            send(folderUrl(btn), 'PATCH', { name: next }, btn);
        },
        deleteFolder: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            send(folderUrl(btn), 'DELETE', null, btn);
        },
        // Mirrors the selected folder's stored defaults into the form. Reading them off the
        // option element avoids a round trip per selection; the server stays the authority
        // when Save posts them back.
        loadDefaults: function (): void {
            const select = document.getElementById('ws-defaults-folder') as HTMLSelectElement | null;
            if (!select) return;
            const option = select.options[select.selectedIndex];
            if (!option) return;

            const set = function (id: string, prop: 'value' | 'checked', attr: string): void {
                const el = document.getElementById(id) as HTMLInputElement | null;
                if (!el) return;
                if (prop === 'checked') {
                    el.checked = attr === 'true';
                } else {
                    el.value = attr;
                }
            };

            set('ws-defaults-expiry', 'value', qsPage.attr(option, 'data-expiry'));
            set('ws-defaults-password', 'checked', qsPage.attr(option, 'data-password'));
            set('ws-defaults-hint', 'value', qsPage.attr(option, 'data-hint'));
            set('ws-defaults-privacy', 'checked', qsPage.attr(option, 'data-privacy'));
            set('ws-defaults-locked', 'checked', qsPage.attr(option, 'data-locked'));
        },
        saveDefaults: function (btn: HTMLButtonElement): void {
            const expiry = qsPage.value('ws-defaults-expiry');
            send('/api/workspace/folders/' + qsPage.value('ws-defaults-folder') + '/defaults', 'PUT', {
                expiryHours: expiry ? parseFloat(expiry) : null,
                passwordRequired: qsPage.checked('ws-defaults-password'),
                passwordHint: qsPage.value('ws-defaults-hint'),
                privacyMode: qsPage.checked('ws-defaults-privacy'),
                locked: qsPage.checked('ws-defaults-locked'),
            }, btn, qsPage.attr(btn, 'data-saved-label'));
        },
        saveTemplate: function (btn: HTMLButtonElement): void {
            send('/api/workspace/templates', 'POST', { name: qsPage.value('ws-template-name') }, btn);
        },
        applyTemplate: function (btn: HTMLButtonElement): void {
            const root = window.prompt(qsPage.attr(btn, 'data-prompt'), '');
            if (root === null) return;
            send(templateUrl(btn, '/apply'), 'POST', { rootName: root || null }, btn);
        },
        deleteTemplate: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            send(templateUrl(btn), 'DELETE', null, btn);
        },
        // Not qsPage.send: accepting an invite navigates away instead of refreshing a section,
        // and reports into the page rather than an alert.
        join: function (btn: HTMLButtonElement): void {
            const kind = qsPage.attr(btn, 'data-kind');
            const url = kind === 'transfer'
                ? '/api/workspace/transfer/accept'
                : '/api/workspace/invites/accept';

            btn.disabled = true;
            fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ token: qsPage.attr(btn, 'data-token') }),
            }).then(function (response) {
                const status = document.getElementById('ws-join-status');
                if (response.ok) {
                    if (status) status.textContent = qsPage.attr(btn, 'data-ok-label');
                    window.location.href = qsPage.attr(btn, 'data-next');
                    return;
                }
                btn.disabled = false;
                if (status) status.textContent = qsPage.attr(btn, 'data-error-label');
            }).catch(function () { btn.disabled = false; });
        },
    };
})();
