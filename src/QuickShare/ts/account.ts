// Avatar preview + profile-updated toast (Account page).
function previewAvatar(e: Event): void {
    const target = e.target as HTMLInputElement;
    const file = target.files && target.files[0];
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

    const reader = new FileReader();
    reader.onload = function (ev: ProgressEvent<FileReader>) {
        const img = document.getElementById('avatarPreview');
        const initial = document.getElementById('avatarInitial');
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