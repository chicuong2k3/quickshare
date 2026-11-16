// Avatar preview (Account page).
function previewAvatar(e: Event): void {
    const target = e.target as HTMLInputElement;
    const file = target.files && target.files[0];
    if (!file) return;

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
    reader.readAsDataURL(file);
}
