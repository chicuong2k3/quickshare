// QuickShare service worker (issue #14): caches the STATIC SHELL only —
// CSS/JS/icons. NEVER caches HTML or HTMX partials: the server
// HtmxCacheMiddleware owns those; two cache systems fighting is the 3am bug.
// Bump SHELL_VERSION on deploy so an old SW does not pin the site.

const SHELL_VERSION = 'qs-shell-v1';

const SHELL = [
    '/css/app.css',
    '/css/QuickShare.styles.css',
    '/js/upload.js',
    '/js/download.js',
    '/js/encryption.js',
    '/js/qrcode.min.js',
    '/js/paste-anywhere.js',
    '/og-cover.svg'
];

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(SHELL_VERSION)
            .then(function (cache) { return cache.addAll(SHELL); })
            .then(function () { return self.skipWaiting(); })
    );
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(
                    keys.filter(function (key) { return key !== SHELL_VERSION; })
                        .map(function (key) { return caches.delete(key); })
                );
            })
            .then(function () { return self.clients.claim(); })
    );
});

self.addEventListener('fetch', function (event) {
    if (event.request.method !== 'GET') return;

    var url = new URL(event.request.url);
    if (url.origin !== self.location.origin) return;

    // Navigations are network-first with an offline shell fallback; HTML is
    // never stored by this SW.
    if (event.request.mode === 'navigate') {
        event.respondWith(
            fetch(event.request)
                .catch(function () { return caches.match('/'); })
        );
        return;
    }

    // Static shell: cache-first; a miss falls through to the network.
    if (SHELL.indexOf(url.pathname) !== -1) {
        event.respondWith(
            caches.match(event.request)
                .then(function (hit) { return hit || fetch(event.request); })
        );
    }
});
