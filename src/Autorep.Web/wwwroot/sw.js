// Service worker: caches the application shell so the PWA loads offline, and runtime-caches
// milk-supply company logos (reference data) so testers can see them offline once viewed.
//
// Phase 2 (M2) will expand this with IndexedDB for Machine Test data + a sync queue, and
// should PROACTIVELY pre-cache all active milk-company logos on reference-data sync (not just
// ones already viewed) and render the tester pages offline so cached logos actually display.

const CACHE_VERSION = 'autorep-v3';
const LOGO_CACHE = 'autorep-logos-v1';
const APP_SHELL = [
  '/manifest.webmanifest',
  '/css/site.css',
  '/js/pwa-register.js',
  '/icons/icon.svg'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then((cache) => cache.addAll(APP_SHELL).catch(() => {}))
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(
        keys.filter((k) => k !== CACHE_VERSION && k !== LOGO_CACHE).map((k) => caches.delete(k))
      )
    )
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;

  const url = new URL(event.request.url);

  // Milk-supply company logos: reference data — cache for offline use
  // (stale-while-revalidate: serve cached immediately, refresh in the background).
  if (url.pathname.startsWith('/api/milk-companies/') && url.pathname.endsWith('/logo')) {
    event.respondWith(
      caches.open(LOGO_CACHE).then((cache) =>
        cache.match(event.request).then((cached) => {
          const network = fetch(event.request)
            .then((resp) => {
              if (resp && resp.ok) cache.put(event.request, resp.clone());
              return resp;
            })
            .catch(() => cached);
          return cached || network;
        })
      )
    );
    return;
  }

  // Other API + Account/auth flows: always go to the network.
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/Account/')) {
    return;
  }

  // Navigation requests: network-first, fall back to cached homepage on offline.
  if (event.request.mode === 'navigate') {
    event.respondWith(
      fetch(event.request).catch(() => caches.match('/') || new Response('Offline', { status: 503 }))
    );
    return;
  }

  // Same-origin static assets: cache-first.
  if (url.origin === self.location.origin) {
    event.respondWith(
      caches.match(event.request).then((cached) =>
        cached || fetch(event.request).catch(() => new Response('', { status: 504 }))
      )
    );
  }
});
