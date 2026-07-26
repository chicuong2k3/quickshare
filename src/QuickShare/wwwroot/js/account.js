function previewAvatar(e) {
        var file = e.target.files[0];
        if (!file) return;
    if (file.size > 5 * 1024 * 1024) { if (window.DAUB) DAUB.toast('Image must be under 5 MB.', 'error'); e.target.value = ''; return; }
    if (!file.type.startsWith('image/')) { if (window.DAUB) DAUB.toast('Please select an image file.', 'error'); e.target.value = ''; return; }
        var reader = new FileReader();
        reader.onload = function (ev) {
            var img = document.getElementById('avatarPreview');
            var initial = document.getElementById('avatarInitial');
            if (img) { img.src = ev.target.result; img.classList.remove('hidden'); }
            if (initial) initial.classList.add('hidden');
        };
        reader.onerror = function () { if (window.DAUB) DAUB.toast('Failed to read image file.', 'error'); e.target.value = ''; };
        reader.readAsDataURL(file);
    }

    document.addEventListener('DOMContentLoaded', function () {
        var state = { loading: true, deliveries: [], editIndex: null };
        var loadingEl = document.getElementById('branding-loading');
        var emptyEl = document.getElementById('branding-empty');
        var listEl = document.getElementById('branding-list');

        function render() {
            loadingEl.style.display = state.loading ? '' : 'none';
            emptyEl.style.display = (!state.loading && state.deliveries.length === 0) ? '' : 'none';
            listEl.style.display = (!state.loading && state.deliveries.length > 0) ? '' : 'none';
            if (!state.loading && state.deliveries.length > 0) {
                listEl.innerHTML = state.deliveries.map(function (d, i) {
                    return '<div class="account-branding-item" style="margin-bottom: 0.75rem">' + renderItem(d, i) + '</div>';
                }).join('');
            }
        }

        function renderItem(d, i) {
            var isEditing = state.editIndex === i;
            var toggleCls = isEditing ? 'account-edit-toggle edit-toggle--active' : 'account-edit-toggle edit-toggle--inactive';
            var toggleText = isEditing ? 'Close' : (d.hasBranding ? 'Edit' : 'Brand');

            var editForm = isEditing ? '\
                <div class="edit-form">\
                    <div class="edit-fields">\
                        <label class="db-field">\
                            <span class="db-label" style="font-size: 0.75rem">Logo URL</span>\
                            <input type="url" id="logo-' + i + '" value="' + escHtml(d.editLogo) + '" class="db-field__input db-input" style="font-size: 0.75rem" />\
                        </label>\
                        <label class="db-field">\
                            <span class="db-label" style="font-size: 0.75rem">Brand message</span>\
                            <input type="text" id="msg-' + i + '" value="' + escHtml(d.editMessage) + '" class="db-field__input db-input" style="font-size: 0.75rem" />\
                        </label>\
                        <div>\
                            <span class="db-label db-block" style="font-size: 0.75rem; margin-bottom: 0.5rem">Accent colour</span>\
                            <div class="color-row">\
                                <input type="color" id="accent-' + i + '" value="' + (d.editAccent || '#6366f1') + '" class="color-swatch" onchange="document.getElementById(\'accentText-' + i + '\').value=this.value" />\
                                <input type="text" id="accentText-' + i + '" value="' + (d.editAccent || '#6366f1') + '" class="db-input color-text" style="font-size: 0.75rem" oninput="document.getElementById(\'accent-' + i + '\').value=this.value" />\
                            </div>\
                        </div>\
                    </div>\
                    <div class="edit-actions">\
                        <button type="button" onclick="saveBranding(' + i + ')" class="db-btn db-btn--primary" style="font-size: 0.75rem" id="save-btn-' + i + '">Save branding</button>\
                        <button type="button" onclick="closeEdit()" class="db-btn db-btn--ghost" style="font-size: 0.75rem">Cancel</button>\
                    </div>\
                    <div id="save-error-' + i + '" class="db-alert db-alert--error" style="font-size: 0.75rem; margin-top: 0.5rem; display:none" role="alert"></div>\
                    <div id="save-success-' + i + '" class="db-alert db-alert--success" style="font-size: 0.75rem; margin-top: 0.5rem; display:none" role="status">Saved!</div>\
                </div>' : '';

            return '\
                <div class="account-branding-item-header">\
                    <div class="account-branding-item-name">\
                        <p class="account-branding-item-filename">' + escHtml(d.fileName) + '</p>\
                        <p style="font-size: 0.75rem; color: var(--color-muted-foreground); margin-top: 0.125rem">' + escHtml(d.shareCode) + '</p>\
                    </div>\
                    <button type="button" onclick="toggleEdit(' + i + ')" class="' + toggleCls + '">' + toggleText + '</button>\
                </div>\
                ' + editForm;
        }

        function escHtml(str) { if (!str) return ''; return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

        async function loadDeliveries() {
            state.loading = true; render();
            try {
                var apiBase = window.ZAPDROP_CONFIG ? window.ZAPDROP_CONFIG.apiBaseUrl : '';
                var r = await fetch(apiBase + '/api/deliveries');
                if (r.ok) {
                    var items = await r.json();
                    state.deliveries = items.map(function (d) { return { id: d.id, fileName: d.fileName, shareCode: d.shareCode, hasBranding: d.hasBranding, editLogo: '', editMessage: '', editAccent: '#6366f1', saveError: '', saveSuccess: false }; });
                }
            } catch (_) {}
            finally { state.loading = false; render(); }
        }

        window.toggleEdit = function (i) {
            if (state.editIndex === i) { state.editIndex = null; }
            else { state.editIndex = i; var d = state.deliveries[i]; d.editLogo = ''; d.editMessage = ''; d.editAccent = '#6366f1'; d.saveError = ''; d.saveSuccess = false; fetchBranding(i); }
            render();
        };
        window.closeEdit = function () { state.editIndex = null; render(); };

        async function fetchBranding(i) {
            var d = state.deliveries[i];
            try {
                var apiBase = window.ZAPDROP_CONFIG ? window.ZAPDROP_CONFIG.apiBaseUrl : '';
                var r = await fetch(apiBase + '/api/deliveries/' + d.shareCode + '/brand');
                if (r.ok) { var data = await r.json(); d.editLogo = data.logo || ''; d.editMessage = data.message || ''; d.editAccent = data.accent || '#6366f1'; render(); }
            } catch (_) {}
        }

        window.saveBranding = async function (i) {
            var d = state.deliveries[i];
            var btn = document.getElementById('save-btn-' + i);
            var errEl = document.getElementById('save-error-' + i);
            var okEl = document.getElementById('save-success-' + i);
            if (btn) { btn.disabled = true; btn.textContent = 'Saving\u2026'; }
            if (errEl) errEl.style.display = 'none';
            if (okEl) okEl.style.display = 'none';
            d.editLogo = document.getElementById('logo-' + i)?.value || '';
            d.editMessage = document.getElementById('msg-' + i)?.value || '';
            d.editAccent = document.getElementById('accent-' + i)?.value || '#6366f1';
            try {
                var apiBase = window.ZAPDROP_CONFIG ? window.ZAPDROP_CONFIG.apiBaseUrl : '';
                var r = await fetch(apiBase + '/api/deliveries/' + d.id + '/brand', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ logo: d.editLogo || null, message: d.editMessage || null, accent: d.editAccent || null }) });
                if (r.ok) { d.hasBranding = true; if (okEl) okEl.style.display = ''; setTimeout(function () { if (okEl) okEl.style.display = 'none'; }, 3000); }
                else { var err = await r.json().catch(function () { return {}; }); if (errEl) { errEl.textContent = err.message || (err.errors && err.errors[0] && err.errors[0].message) || 'Failed to save.'; errEl.style.display = ''; } }
            } catch (e) { if (errEl) { errEl.textContent = 'Network error.'; errEl.style.display = ''; } }
            finally { if (btn) { btn.disabled = false; btn.textContent = 'Save branding'; } }
        };

    loadDeliveries();
});

document.body.addEventListener('profileUpdated', function () {
    if (window.DAUB) DAUB.toast('Profile updated.', 'success');
});
