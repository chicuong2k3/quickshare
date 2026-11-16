// Without JavaScript the real form POSTs to the SSR handler and redirects back.
// With Unpoly loaded, intercept only this form: the API returns JSON, not HTML.
document.addEventListener('submit', (event: SubmitEvent) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches('[data-notification-read-all]')) {
        return;
    }

    const client = (window as Window & {
        up?: { reload: (selector: string, options: { cache: boolean }) => Promise<unknown> };
    }).up;
    if (!client) {
        return;
    }

    const button = form.querySelector<HTMLButtonElement>('button[type="submit"]');
    if (!button) {
        return;
    }
    event.preventDefault();
    button.disabled = true;

    const readAllRoute = '/api/notifications/read-all';
    const bodySelector = '#notifications-body';
    fetch(readAllRoute, { method: 'POST' })
        .then(async (response) => {
            if (!response.ok) {
                throw new Error(String(response.status));
            }
            await client.reload(bodySelector, { cache: false });
        })
        .catch(() => {
            button.disabled = false;
            window.alert(button.getAttribute('data-error') || '');
        });
});
