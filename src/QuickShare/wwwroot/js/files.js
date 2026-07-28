(function () {
    'use strict';
    window.filesPage = (function () {
        var state = { draggedFileId: null, dragOverFolderId: null };

        function toast(msg, type) { if (window.DAUB) DAUB.toast(msg, type); }

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
                    if (resp.ok) { toast('Moved to folder', 'success'); setTimeout(function () { window.location.href = '/files' + (window.location.search || ''); }, 600); }
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
                    if (resp.ok) { window.location.href = '/files'; }
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
                    if (resp.ok) { window.location.href = '/files' + (window.location.search || ''); }
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
                    if (resp.ok) { var cf = new URLSearchParams(window.location.search).get('folderId'); if (cf === id) window.location.href = '/files'; else window.location.href = '/files' + (window.location.search || ''); }
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
