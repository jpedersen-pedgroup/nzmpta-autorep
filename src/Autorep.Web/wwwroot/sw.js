// Phase 1: minimal service worker that caches the application shell so the
// PWA loads when offline. Phase 2 will expand this to a proper offline-first
// strategy with IndexedDB for Machine Test data and a sync queue.

const CACHE_VERSION = 'autorep-v1';
const APP_SHELL = [
  '/manifest.webmanifest',
  '/css/site.css',
  '/js/site.js',
  '/js/pwa-register.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/jquery/dist/jquery.min.js',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
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
      Promise.all(keys.filter((k) => k !== CACHE_VERSION).map((k) => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;

  const url = new URL(event.request.url);

  // Don't intercept API or Account/auth flows — they need fresh server responses.
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
