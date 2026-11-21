(() => {
    const MAX_AVATAR_BYTES = 5 * 1024 * 1024;
    const NAME_DEBOUNCE_MS = 600;
    const SAVED_CLEAR_MS = 2500;

    interface AvatarRollbackState {
        prevSrc: string;
        prevPreviewHidden: boolean;
        prevInitialHidden: boolean;
    }

    function initProfileAutoSave(): void {
        const form = document.getElementById('profile-form') as HTMLFormElement | null;
        if (!form || form.dataset.qsProfileBound === '1') {
            return;
        }
        form.dataset.qsProfileBound = '1';

        const nameInput = document.getElementById('name') as HTMLInputElement | null;
        const fileInput = document.getElementById('avatarFileInput') as HTMLInputElement | null;
        const avatarPreview = document.getElementById('avatarPreview') as HTMLImageElement | null;
        const avatarInitial = document.getElementById('avatarInitial') as HTMLElement | null;
        const displayNameEl = document.getElementById('profile-display-name') as HTMLElement | null;
        const errorEl = document.getElementById('profile-error') as HTMLElement | null;
        const statusEl = document.getElementById('profile-save-status') as HTMLElement | null;

        if (!nameInput || !fileInput) {
            return;
        }

        let lastSavedName = nameInput.value.trim();
        let inFlight = false;
        let pendingSave = false;
        let debounceTimer: number | null = null;
        let savedClearTimer: number | null = null;

        const showError = (message: string): void => {
            if (!errorEl) return;
            errorEl.textContent = message;
            errorEl.classList.remove('hidden');
        };

        const hideError = (): void => {
            if (!errorEl) return;
            errorEl.textContent = '';
            errorEl.classList.add('hidden');
        };

        const setStatus = (text: string, state: 'idle' | 'saving' | 'saved'): void => {
            if (!statusEl) return;
            if (savedClearTimer !== null) {
                window.clearTimeout(savedClearTimer);
                savedClearTimer = null;
            }
            statusEl.classList.remove('profile-save-status--saving', 'profile-save-status--saved');
            if (state === 'saving') {
                statusEl.classList.add('profile-save-status--saving');
            } else if (state === 'saved') {
                statusEl.classList.add('profile-save-status--saved');
                savedClearTimer = window.setTimeout(() => {
                    statusEl.textContent = '';
                    statusEl.classList.remove('profile-save-status--saved');
                    savedClearTimer = null;
                }, SAVED_CLEAR_MS);
            }
            statusEl.textContent = text;
        };

        const rollbackAvatar = (rollback?: AvatarRollbackState): void => {
            if (!rollback || !avatarPreview) return;
            if (rollback.prevSrc) {
                avatarPreview.src = rollback.prevSrc;
            } else {
                avatarPreview.removeAttribute('src');
            }
            avatarPreview.classList.toggle('hidden', rollback.prevPreviewHidden);
            if (avatarInitial) {
                avatarInitial.classList.toggle('hidden', rollback.prevInitialHidden);
            }
        };

        const syncNavbar = (submittedName: string, serverAvatarSrc: string | null): void => {
            const navName = document.querySelector('.layout-user-info__name');
            if (navName) {
                navName.textContent = submittedName;
            }
            if (!serverAvatarSrc) {
                return;
            }
            const existingNavImg = document.querySelector('.layout-avatar-img') as HTMLImageElement | null;
            if (existingNavImg) {
                existingNavImg.src = serverAvatarSrc;
                existingNavImg.alt = submittedName;
                return;
            }
            const wrap = document.querySelector('.layout-avatar-img-wrap');
            if (wrap) {
                const img = document.createElement('img');
                img.className = 'layout-avatar-img';
                img.src = serverAvatarSrc;
                img.alt = submittedName;
                wrap.prepend(img);
            }
        };

        const saveProfile = async (hasAvatar: boolean, rollback?: AvatarRollbackState): Promise<void> => {
            const submittedName = nameInput.value.trim();
            if (!submittedName) {
                if (hasAvatar) {
                    rollbackAvatar(rollback);
                    fileInput.value = '';
                }
                showError(form.dataset.errorNameRequired || 'Name is required');
                setStatus('', 'idle');
                return;
            }

            if (inFlight) {
                pendingSave = true;
                return;
            }

            inFlight = true;
            hideError();
            setStatus(form.dataset.savingLabel || 'Saving...', 'saving');

            try {
                const formData = new FormData(form);
                formData.set('name', submittedName);
                const actionUrl = form.getAttribute('action') || window.location.pathname;
                const response = await fetch(actionUrl, {
                    method: 'POST',
                    body: formData,
                    credentials: 'same-origin',
                });

                if (!response.ok) {
                    showError(form.dataset.errorUpdateFailed || 'Failed to update profile');
                    setStatus('', 'idle');
                    if (hasAvatar) {
                        rollbackAvatar(rollback);
                        fileInput.value = '';
                    }
                    return;
                }

                const html = await response.text();
                const doc = new DOMParser().parseFromString(html, 'text/html');
                const serverError = doc.getElementById('profile-error')?.textContent?.trim() || '';

                if (serverError) {
                    showError(serverError);
                    setStatus('', 'idle');
                    if (hasAvatar) {
                        rollbackAvatar(rollback);
                        fileInput.value = '';
                    }
                    return;
                }

                lastSavedName = submittedName;
                if (displayNameEl) {
                    displayNameEl.textContent = submittedName;
                }
                if (avatarInitial) {
                    avatarInitial.textContent = submittedName.charAt(0).toUpperCase() || '?';
                }

                const serverPreview = doc.getElementById('avatarPreview') as HTMLImageElement | null;
                const serverAvatarSrc = serverPreview?.getAttribute('src')?.trim() || null;
                if (serverAvatarSrc && avatarPreview) {
                    avatarPreview.src = serverAvatarSrc;
                    avatarPreview.alt = submittedName;
                    avatarPreview.classList.remove('hidden');
                    if (avatarInitial) {
                        avatarInitial.classList.add('hidden');
                    }
                }

                syncNavbar(submittedName, serverAvatarSrc);

                const serverToken = (doc.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement | null)?.value;
                const localToken = form.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement | null;
                if (serverToken && localToken) {
                    localToken.value = serverToken;
                }

                fileInput.value = '';
                setStatus(form.dataset.savedLabel || 'Saved', 'saved');
            } catch {
                showError(form.dataset.errorUpdateFailed || 'Failed to update profile');
                setStatus('', 'idle');
                if (hasAvatar) {
                    rollbackAvatar(rollback);
                    fileInput.value = '';
                }
            } finally {
                inFlight = false;
                if (pendingSave) {
                    pendingSave = false;
                    if (nameInput.value.trim() !== lastSavedName) {
                        void saveProfile(false);
                    }
                }
            }
        };

        fileInput.addEventListener('change', () => {
            const file = fileInput.files && fileInput.files[0];
            if (!file) return;

            if (file.size > MAX_AVATAR_BYTES) {
                showError(form.dataset.errorImageSize || 'Image must be under 5 MB');
                fileInput.value = '';
                return;
            }

            const rollback: AvatarRollbackState = {
                prevSrc: avatarPreview?.getAttribute('src') || '',
                prevPreviewHidden: avatarPreview ? avatarPreview.classList.contains('hidden') : true,
                prevInitialHidden: avatarInitial ? avatarInitial.classList.contains('hidden') : false,
            };

            const reader = new FileReader();
            reader.onload = (ev: ProgressEvent<FileReader>) => {
                const dataUrl = (ev.target as FileReader | null)?.result;
                if (typeof dataUrl === 'string' && avatarPreview) {
                    avatarPreview.src = dataUrl;
                    avatarPreview.classList.remove('hidden');
                }
                if (avatarInitial) {
                    avatarInitial.classList.add('hidden');
                }
            };
            reader.readAsDataURL(file);

            void saveProfile(true, rollback);
        });

        nameInput.addEventListener('input', () => {
            hideError();
            if (debounceTimer !== null) {
                window.clearTimeout(debounceTimer);
                debounceTimer = null;
            }
            const trimmed = nameInput.value.trim();
            if (trimmed.length > 0 && trimmed !== lastSavedName) {
                debounceTimer = window.setTimeout(() => {
                    debounceTimer = null;
                    const current = nameInput.value.trim();
                    if (current.length > 0 && current !== lastSavedName) {
                        void saveProfile(false);
                    }
                }, NAME_DEBOUNCE_MS);
            }
        });

        nameInput.addEventListener('blur', () => {
            if (debounceTimer !== null) {
                window.clearTimeout(debounceTimer);
                debounceTimer = null;
            }
            const trimmed = nameInput.value.trim();
            if (trimmed.length === 0) {
                showError(form.dataset.errorNameRequired || 'Name is required');
                return;
            }
            if (trimmed !== lastSavedName) {
                void saveProfile(false);
            }
        });

        form.addEventListener('submit', (e: Event) => {
            e.preventDefault();
            if (debounceTimer !== null) {
                window.clearTimeout(debounceTimer);
                debounceTimer = null;
            }
            const trimmed = nameInput.value.trim();
            if (trimmed.length === 0) {
                showError(form.dataset.errorNameRequired || 'Name is required');
                return;
            }
            void saveProfile(false);
        });
    }

    if (document.readyState !== 'loading') {
        initProfileAutoSave();
    } else {
        document.addEventListener('DOMContentLoaded', initProfileAutoSave);
    }
})();
