(function () {
    'use strict';
    window.filesPage = (function () {
        var state = { draggedFileId: null, dragOverFolderId: null };

        function apiBase() { return (window.ZAPDROP_CONFIG && window.ZAPDROP_CONFIG.apiBaseUrl || '').replace(/\/+$/, ''); }
        function toast(msg, type) { if (window.DAUB) DAUB.toast(msg, type); }

        // Close the given folder modal by activating its close button, which routes
        // through the Alpine closeModals() handler in Files.razor.
        function closeModal(which) {
            var btn = document.querySelector('#files-modal-' + which + ' .modal-close');
            if (btn) btn.click();
        }

        // Refresh only the #files-content region (folder list, stats, file list) via an
        // HTMX partial request — same mechanism the in-page folder/search links use.
        // Falls back to a full navigation when HTMX is unavailable.
        function refreshContent() {
            var url = window.location.pathname + window.location.search;
            try {
                var content = document.getElementById('files-content');
                if (window.htmx && content) {
                    // Drive a throwaway element through the same hx-get/hx-target/hx-select
                    // pipeline as the page's own partial links (folder navigation, search).
                    var holder = document.createElement('div');
                    holder.setAttribute('hx-get', url);
                    holder.setAttribute('hx-trigger', 'load');
                    holder.setAttribute('hx-target', '#files-content');
                    holder.setAttribute('hx-select', '#files-content');
                    holder.setAttribute('hx-swap', 'outerHTML');
                    document.body.appendChild(holder);
                    htmx.process(holder);
                    holder.addEventListener('htmx:afterSettle', function () {
                        holder.remove();
                    });
                    return;
                }
            } catch (_) { }
            window.location.href = url;
        }

        return {
            onDragStart: function (e, fileId) { state.draggedFileId = fileId; e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', fileId); },
            onDragEnd: function () { state.draggedFileId = null; state.dragOverFolderId = null; },
            onDragOver: function (e, folderId) { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; },
            onDragEnter: function (folderId) { state.dragOverFolderId = folderId; },
            onDragLeave: function (folderId) { if (state.dragOverFolderId === folderId) state.dragOverFolderId = null; },
            onDrop: async function (e, folderId) {
                state.dragOverFolderId = null;
                var fileId = state.draggedFileId || e.dataTransfer.getData('text/plain');
                state.draggedFileId = null;
                if (!fileId || !folderId) return;
                try {
                    var resp = await fetch(apiBase() + '/api/files/' + fileId + '/folder', { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify({ fileId: fileId, targetFolderId: folderId }) });
                    if (resp.ok) { toast('Moved to folder', 'success'); refreshContent(); }
                    else { var t = await resp.text(); var msg = 'Failed to move file'; try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {} toast(msg, 'error'); }
                } catch (_) { toast('Network error', 'error'); }
            },

            submitCreateFolder: async function () {
                var name = document.getElementById('create-folder-name').value.trim();
                if (!name) return;
                document.getElementById('create-btn-text').style.display = 'none';
                document.getElementById('create-btn-loading').style.display = '';
                document.getElementById('create-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders', { method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify({ name: name }) });
                    if (resp.ok) {
                        closeModal('create');
                        refreshContent();
                    }
                    else {
                        var t = await resp.text(); var msg = 'Failed to create folder';
                        try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {}
                        document.getElementById('create-error').textContent = msg;
                        document.getElementById('create-error').style.display = '';
                    }
                } catch (_) {
                    document.getElementById('create-error').textContent = 'Network error';
                    document.getElementById('create-error').style.display = '';
                }
                document.getElementById('create-btn-text').style.display = '';
                document.getElementById('create-btn-loading').style.display = 'none';
                document.getElementById('create-submit-btn').disabled = false;
            },

            submitRenameFolder: async function (id) {
                var name = document.getElementById('rename-folder-name').value.trim();
                if (!name || !id) return;
                document.getElementById('rename-btn-text').style.display = 'none';
                document.getElementById('rename-btn-loading').style.display = '';
                document.getElementById('rename-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders/' + id, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify({ id: id, name: name }) });
                    if (resp.ok) {
                        closeModal('rename');
                        refreshContent();
                    }
                    else {
                        var t = await resp.text(); var msg = 'Failed to rename';
                        try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {}
                        document.getElementById('rename-error').textContent = msg;
                        document.getElementById('rename-error').style.display = '';
                    }
                } catch (_) {
                    document.getElementById('rename-error').textContent = 'Network error';
                    document.getElementById('rename-error').style.display = '';
                }
                document.getElementById('rename-btn-text').style.display = '';
                document.getElementById('rename-btn-loading').style.display = 'none';
                document.getElementById('rename-submit-btn').disabled = false;
            },

            submitDeleteFolder: async function (id) {
                if (!id) return;
                document.getElementById('delete-btn-text').style.display = 'none';
                document.getElementById('delete-btn-loading').style.display = '';
                document.getElementById('delete-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders/' + id, { method: 'DELETE', credentials: 'include' });
                    if (resp.ok) {
                        closeModal('delete');
                        var cf = new URLSearchParams(window.location.search).get('folderId');
                        if (cf === id) {
                            // Navigating away from the deleted folder: update the URL bar, then refresh.
                            if (window.history && history.replaceState) history.replaceState(null, '', '/files');
                        }
                        refreshContent();
                    }
                    else {
                        var t = await resp.text(); var msg = 'Failed to delete';
                        try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {}
                        document.getElementById('delete-error').textContent = msg;
                        document.getElementById('delete-error').style.display = '';
                    }
                } catch (_) {
                    document.getElementById('delete-error').textContent = 'Network error';
                    document.getElementById('delete-error').style.display = '';
                }
                document.getElementById('delete-btn-text').style.display = '';
                document.getElementById('delete-btn-loading').style.display = 'none';
                document.getElementById('delete-submit-btn').disabled = false;
            }
        };
    })();
})();

// After an HTMX swap brings new markup into #files-content (folder list, file list),
// re-bind Alpine directives (@click etc.) on the swapped subtree.
document.addEventListener('htmx:afterSwap', function (e) {
    if (window.Alpine && e.detail && e.detail.target) {
        Alpine.initTree(e.detail.target);
    }
});
