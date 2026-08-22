// Passkeys (issue #68) and magic-link sign-in (issue #132). One global object, page-scoped.
//
// WebAuthn speaks ArrayBuffers and the wire speaks base64url, so most of this file is that
// translation. Getting it wrong produces "the authenticator said no" with no further detail,
// which is why the conversions live in one place rather than being inlined at each call.
window.quickSharePasskeys = (function () {
    const BODY = '#security-body';

    function toBuffer(base64url: string): ArrayBuffer {
        const padded = base64url.replace(/-/g, '+').replace(/_/g, '/');
        const binary = window.atob(padded + '==='.slice((padded.length + 3) % 4));
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes.buffer;
    }

    function toBase64Url(buffer: ArrayBuffer): string {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return window.btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    /** True when this browser can do WebAuthn at all. */
    function supported(): boolean {
        return typeof window.PublicKeyCredential !== 'undefined' && !!navigator.credentials;
    }

    function postJson(url: string, payload: unknown): Promise<any | null> {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: payload === null ? undefined : JSON.stringify(payload),
        }).then(function (response) {
            if (!response.ok) return null;
            return response.status === 204 ? {} : response.json();
        }).catch(function () { return null; });
    }

    return {
        register: function (btn: HTMLButtonElement): void {
            if (!supported()) {
                window.alert(qsPage.attr(btn, 'data-unsupported-label'));
                return;
            }

            btn.disabled = true;
            postJson('/api/account/passkeys/register/begin', null)
                .then(function (begin: { handle: string; options: any } | null) {
                    if (!begin) throw new Error('begin failed');

                    const options = begin.options;
                    options.challenge = toBuffer(options.challenge);
                    options.user.id = toBuffer(options.user.id);
                    (options.excludeCredentials || []).forEach(function (credential: any) {
                        credential.id = toBuffer(credential.id);
                    });

                    return navigator.credentials.create({ publicKey: options })
                        .then(function (credential) {
                            const created = credential as PublicKeyCredential;
                            const attestation = created.response as AuthenticatorAttestationResponse;

                            return postJson('/api/account/passkeys/register/complete', {
                                handle: begin.handle,
                                name: qsPage.value('passkey-name'),
                                response: {
                                    id: created.id,
                                    rawId: toBase64Url(created.rawId),
                                    type: created.type,
                                    extensions: created.getClientExtensionResults(),
                                    response: {
                                        attestationObject: toBase64Url(attestation.attestationObject),
                                        clientDataJson: toBase64Url(attestation.clientDataJSON),
                                    },
                                },
                            });
                        });
                })
                .then(function (result) {
                    btn.disabled = false;
                    if (!result) {
                        window.alert(qsPage.attr(btn, 'data-error-label'));
                        return;
                    }
                    qsPage.reload(BODY);
                })
                .catch(function () {
                    btn.disabled = false;
                    // Includes the user simply cancelling the browser prompt, which is not an
                    // error worth shouting about.
                });
        },

        remove: function (btn: HTMLButtonElement): void {
            if (!window.confirm(qsPage.attr(btn, 'data-confirm'))) return;
            qsPage.send('/api/account/passkeys/' + qsPage.attr(btn, 'data-id'), 'DELETE', null,
                { btn: btn, body: BODY });
        },

        signIn: function (btn: HTMLButtonElement): void {
            if (!supported()) {
                window.alert(qsPage.attr(btn, 'data-unsupported-label'));
                return;
            }

            btn.disabled = true;
            postJson('/api/auth/passkey/begin', null)
                .then(function (begin: { handle: string; options: any } | null) {
                    if (!begin) throw new Error('begin failed');

                    const options = begin.options;
                    options.challenge = toBuffer(options.challenge);
                    (options.allowCredentials || []).forEach(function (credential: any) {
                        credential.id = toBuffer(credential.id);
                    });

                    return navigator.credentials.get({ publicKey: options })
                        .then(function (credential) {
                            const asserted = credential as PublicKeyCredential;
                            const assertion = asserted.response as AuthenticatorAssertionResponse;

                            return postJson('/api/auth/passkey/complete', {
                                handle: begin.handle,
                                response: {
                                    id: asserted.id,
                                    rawId: toBase64Url(asserted.rawId),
                                    type: asserted.type,
                                    extensions: asserted.getClientExtensionResults(),
                                    response: {
                                        authenticatorData: toBase64Url(assertion.authenticatorData),
                                        clientDataJson: toBase64Url(assertion.clientDataJSON),
                                        signature: toBase64Url(assertion.signature),
                                        userHandle: assertion.userHandle ? toBase64Url(assertion.userHandle) : null,
                                    },
                                },
                            });
                        });
                })
                .then(function (result) {
                    btn.disabled = false;
                    if (!result) {
                        window.alert(qsPage.attr(btn, 'data-error-label'));
                        return;
                    }
                    window.location.href = '/';
                })
                .catch(function () { btn.disabled = false; });
        },

        // Always reports the same thing back, matching what the endpoint does: the response must
        // not reveal whether an address has an account.
        sendLink: function (btn: HTMLButtonElement): void {
            const email = qsPage.value('login-email');
            if (!email) return;

            btn.disabled = true;
            postJson('/api/auth/magic-link', { email: email }).then(function () {
                btn.disabled = false;
                window.alert(qsPage.attr(btn, 'data-sent-label'));
            });
        },
    };
})();
