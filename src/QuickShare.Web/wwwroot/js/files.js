(function () {
    'use strict';
    window.filesPage = (function () {
        var state = { draggedFileId: null, dragOverFolderId: null, renameId: '', deleteId: '' };

        function show(id) { var el = document.getElementById(id); if (el) el.classList.remove('hidden'); }
        function hide(id) { var el = document.getElementById(id); if (el) el.classList.add('hidden'); }

        function toast(msg, type) { if (window.DAUB) DAUB.toast(msg, type); }

        function resetCreateModal() { document.getElementById('create-folder-name').value = ''; hide('create-error'); hide('create-btn-loading'); show('create-btn-text'); document.getElementById('create-submit-btn').disabled = false; }
        function resetRenameModal() { document.getElementById('rename-folder-name').value = ''; hide('rename-error'); hide('rename-btn-loading'); show('rename-btn-text'); document.getElementById('rename-submit-btn').disabled = false; }
        function resetDeleteModal() { hide('delete-error'); hide('delete-btn-loading'); show('delete-btn-text'); document.getElementById('delete-submit-btn').disabled = false; }

        return {
            openCreateFolder: function () { resetCreateModal(); show('files-modal-create'); document.getElementById('create-folder-name').focus(); },
            openRenameFolder: function (id, name) { resetRenameModal(); state.renameId = id; document.getElementById('rename-folder-name').value = name; show('files-modal-rename'); document.getElementById('rename-folder-name').focus(); document.getElementById('rename-folder-name').select(); },
            openDeleteFolder: function (id, name) { resetDeleteModal(); state.deleteId = id; document.getElementById('delete-name').textContent = name; show('files-modal-delete'); },
            closeModals: function () { hide('files-modal-create'); hide('files-modal-rename'); hide('files-modal-delete'); },

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
                    if (resp.ok) { toast('Moved to folder', 'success'); setTimeout(function () { window.location.href = '/files' + (window.location.search || ''); }, 600); }
                    else { var t = await resp.text(); var msg = 'Failed to move file'; try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {} toast(msg, 'error'); }
                } catch (_) { toast('Network error', 'error'); }
            },

            submitCreateFolder: async function () {
                var name = document.getElementById('create-folder-name').value.trim();
                if (!name) return;
                hide('create-btn-text'); show('create-btn-loading'); document.getElementById('create-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders', { method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify({ name: name }) });
                    if (resp.ok) { window.location.href = '/files'; } else { var t = await resp.text(); var msg = 'Failed to create folder'; try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {} document.getElementById('create-error').textContent = msg; show('create-error'); }
                } catch (_) { document.getElementById('create-error').textContent = 'Network error'; show('create-error'); }
                hide('create-btn-loading'); show('create-btn-text'); document.getElementById('create-submit-btn').disabled = false;
            },

            submitRenameFolder: async function () {
                var name = document.getElementById('rename-folder-name').value.trim();
                if (!name || !state.renameId) return;
                hide('rename-btn-text'); show('rename-btn-loading'); document.getElementById('rename-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders/' + state.renameId, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify({ id: state.renameId, name: name }) });
                    if (resp.ok) { window.location.href = '/files' + (window.location.search || ''); } else { var t = await resp.text(); var msg = 'Failed to rename'; try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {} document.getElementById('rename-error').textContent = msg; show('rename-error'); }
                } catch (_) { document.getElementById('rename-error').textContent = 'Network error'; show('rename-error'); }
                hide('rename-btn-loading'); show('rename-btn-text'); document.getElementById('rename-submit-btn').disabled = false;
            },

            submitDeleteFolder: async function () {
                if (!state.deleteId) return;
                hide('delete-btn-text'); show('delete-btn-loading'); document.getElementById('delete-submit-btn').disabled = true;
                try {
                    var resp = await fetch(apiBase() + '/api/folders/' + state.deleteId, { method: 'DELETE', credentials: 'include' });
                    if (resp.ok) { var cf = new URLSearchParams(window.location.search).get('folderId'); if (cf === state.deleteId) window.location.href = '/files'; else window.location.href = '/files' + (window.location.search || ''); }
                    else { var t = await resp.text(); var msg = 'Failed to delete'; try { var err = JSON.parse(t); msg = err.message || err.title || msg; } catch (_) {} document.getElementById('delete-error').textContent = msg; show('delete-error'); }
                } catch (_) { document.getElementById('delete-error').textContent = 'Network error'; show('delete-error'); }
                hide('delete-btn-loading'); show('delete-btn-text'); document.getElementById('delete-submit-btn').disabled = false;
            }
        };
    })();
})();