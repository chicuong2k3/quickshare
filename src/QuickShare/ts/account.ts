// Avatar preview + profile-updated toast (Account page).
function previewAvatar(e: Event): void {
    var target = e.target as HTMLInputElement;
    var file = target.files && target.files[0];
    if (!file) return;

    if (file.size > 5 * 1024 * 1024) {
        if (window.DAUB) window.DAUB.toast('Image must be under 5 MB.', 'error');
        target.value = '';
        return;
    }

    if (!file.type.startsWith('image/')) {
        if (window.DAUB) window.DAUB.toast('Please select an image file.', 'error');
        target.value = '';
        return;
    }

    var reader = new FileReader();
    reader.onload = function (ev: ProgressEvent<FileReader>) {
        var img = document.getElementById('avatarPreview');
        var initial = document.getElementById('avatarInitial');
        if (img) {
            (img as HTMLImageElement).src = (ev.target as FileReader).result as string;
            img.classList.remove('hidden');
        }
        if (initial) initial.classList.add('hidden');
    };
    reader.onerror = function () {
        if (window.DAUB) window.DAUB.toast('Failed to read image file.', 'error');
        target.value = '';
    };
    reader.readAsDataURL(file);
}

document.body.addEventListener('profileUpdated', function () {
    if (window.DAUB) window.DAUB.toast('Profile updated.', 'success');
});