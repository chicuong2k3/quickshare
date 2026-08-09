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

document.body.addEventListener('profileUpdated', function () {
    if (window.DAUB) DAUB.toast('Profile updated.', 'success');
});