// Minimal service worker: exists mainly to satisfy PWA/TWA installability criteria, not to take
// this live-data warehouse app offline. Only the static app shell is cached (cache-first, since
// those files are fingerprinted/versioned by the build and safe to reuse); every other request
// (pages, any API calls) goes straight to the network - we never want a Picker looking at a
// cached, stale allocation result or inventory count.
const CACHE_NAME = "wms-shell-v1";
const SHELL_ASSETS = [
    "/manifest.webmanifest",
    "/icons/icon-192.png",
    "/icons/icon-512.png",
    "/icons/icon-512-maskable.png",
    "/favicon.png"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(SHELL_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    const url = new URL(event.request.url);
    const isShellAsset = event.request.method === "GET" && SHELL_ASSETS.includes(url.pathname);

    if (!isShellAsset) {
        return; // let the browser handle everything else (pages, SignalR, downloads) normally
    }

    event.respondWith(
        caches.match(event.request).then((cached) => cached ?? fetch(event.request))
    );
});
