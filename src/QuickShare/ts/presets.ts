/**
 * Upload presets page (#100).
 *
 * Handles create + delete on /account/presets, and exposes fetchPresets() / loadPreset()
 * for the upload page to populate and apply a preset.
 */

interface PresetDto {
    id: string;
    name: string;
    expiryHours: number | null;
    password: string | null;
    burnAfterDownload: boolean;
    privacyMode: boolean;
    recipients: string | null;
}

const quickSharePresets = {
    async create(btn: HTMLButtonElement): Promise<void> {
        const name = (document.getElementById('preset-name') as HTMLInputElement)?.value?.trim();
        const expiryHours = (document.getElementById('preset-expiry') as HTMLSelectElement)?.value;
        const password = (document.getElementById('preset-password') as HTMLInputElement)?.value?.trim();
        const burn = (document.getElementById('preset-burn') as HTMLInputElement)?.checked ?? false;
        const privacy = (document.getElementById('preset-privacy') as HTMLInputElement)?.checked ?? false;
        const recipients = (document.getElementById('preset-recipients') as HTMLInputElement)?.value?.trim();
        const errorEl = document.getElementById('preset-error');

        if (!name) {
            if (errorEl) { errorEl.textContent = 'Name is required.'; errorEl.classList.remove('hidden'); }
            return;
        }
        if (errorEl) errorEl.classList.add('hidden');
        btn.disabled = true;

        const body = {
            name,
            expiryHours: expiryHours ? parseFloat(expiryHours) : null,
            password: password || null,
            burnAfterDownload: burn,
            privacyMode: privacy,
            recipients: recipients || null,
        };

        try {
            const res = await fetch('/api/upload-presets', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
            });
            if (res.ok) {
                window.location.reload();
            } else {
                const err = await res.json().catch(() => ({})) as Record<string, unknown>;
                if (errorEl) {
                    errorEl.textContent = String(err.message ?? 'Failed to save preset.');
                    errorEl.classList.remove('hidden');
                }
            }
        } catch {
            if (errorEl) { errorEl.textContent = 'Network error.'; errorEl.classList.remove('hidden'); }
        } finally {
            btn.disabled = false;
        }
    },

    async delete(btn: HTMLButtonElement): Promise<void> {
        const id = btn.dataset['id'];
        const confirmMsg = btn.dataset['confirm'];
        if (!id || !window.confirm(confirmMsg ?? 'Delete this preset?')) return;
        btn.disabled = true;
        try {
            const res = await fetch(`/api/upload-presets/${id}`, { method: 'DELETE' });
            if (res.ok) {
                const item = document.getElementById(`preset-${id}`);
                item?.remove();
            }
        } finally {
            btn.disabled = false;
        }
    },

    /** Populate the upload page's preset selector (called on page load or refresh). */
    async fetchPresets(): Promise<void> {
        const select = document.getElementById('upload-preset-select') as HTMLSelectElement | null;
        if (!select) return;
        try {
            const res = await fetch('/api/upload-presets');
            if (!res.ok) return;
            const presets = await res.json() as PresetDto[];
            // Rebuild options, keeping the empty sentinel
            while (select.options.length > 1) select.remove(1);
            for (const p of presets) {
                const opt = document.createElement('option');
                opt.value = p.id;
                opt.textContent = p.name;
                opt.dataset['expiryHours'] = p.expiryHours != null ? String(p.expiryHours) : '';
                opt.dataset['password'] = p.password ?? '';
                opt.dataset['burn'] = String(p.burnAfterDownload);
                opt.dataset['privacy'] = String(p.privacyMode);
                opt.dataset['recipients'] = p.recipients ?? '';
                select.appendChild(opt);
            }
        } catch {
            // silent: presets are optional
        }
    },

    /** Apply the selected preset's settings to the upload form. */
    loadPreset(select: HTMLSelectElement): void {
        const opt = select.selectedOptions[0];
        if (!opt?.value) return;

        const set = (id: string, val: string) => {
            const el = document.getElementById(id) as HTMLInputElement | HTMLSelectElement | null;
            if (el) el.value = val;
        };
        const check = (id: string, val: boolean) => {
            const el = document.getElementById(id) as HTMLInputElement | null;
            if (el) el.checked = val;
        };

        const expiryHours = opt.dataset['expiryHours'] ?? '';
        const password = opt.dataset['password'] ?? '';
        const burn = opt.dataset['burn'] === 'true';
        const privacy = opt.dataset['privacy'] === 'true';
        const recipients = opt.dataset['recipients'] ?? '';

        // Expiry: numeric input (Pro) or select (Free)
        if (expiryHours) {
            const expiryDays = document.getElementById('upload-expiry-days') as HTMLInputElement | null;
            const expirySel = document.getElementById('upload-expiry') as HTMLSelectElement | null;
            const hours = parseFloat(expiryHours);
            if (expiryDays) expiryDays.value = String(Math.max(1, Math.round(hours / 24)));
            if (expirySel) {
                // Try to find an exact match, fall back to closest
                const match = Array.from(expirySel.options).find(o => o.value === expiryHours);
                if (match) expirySel.value = expiryHours;
            }
        }

        set('upload-password', password);
        check('upload-burn', burn);
        check('upload-privacy-mode', privacy);
        if (recipients) set('upload-recipients', recipients);

        // Visual feedback
        select.style.borderColor = 'var(--db-success, green)';
        setTimeout(() => { select.style.borderColor = ''; }, 1500);
    },
};

window.quickSharePresets = quickSharePresets;

// Auto-fetch presets when the upload page loads
if (document.getElementById('upload-preset-select')) {
    quickSharePresets.fetchPresets();
}
