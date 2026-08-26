// Service worker: caches the application shell so the PWA loads offline, and runtime-caches
// milk-supply company logos (reference data) so testers can see them offline once viewed.
//
// Phase 2 (M2) will expand this with IndexedDB for Machine Test data + a sync queue, and
// should PROACTIVELY pre-cache all active milk-company logos on reference-data sync (not just
// ones already viewed) and render the tester pages offline so cached logos actually display.

// Rewritten by tools/stamp-sw.mjs on every client build with a fingerprint of the bundle and of the
// APP_SHELL files below — do not edit by hand. The static cache is cache-first and matched with
// ignoreSearch, so renaming this cache is the ONLY thing that retires a previous build's assets.
// The stamper reads APP_SHELL out of this file, and every entry must be a real file under wwwroot
// so it can be hashed; a served route would build green and then never cache-bust.
const CACHE_VERSION = 'autorep-eef0d30d5e02';
const LOGO_CACHE = 'autorep-logos-v1';
const FA_CACHE = 'autorep-fontawesome-v1';
const APP_SHELL = [
  '/manifest.webmanifest',
  '/css/site.css',
  '/js/pwa-register.js',
  '/icons/icon.svg',
  '/img/logo-mpnz.svg',
  '/img/logo-mpnz-white.svg',
  '/img/logo-mpnz-lockup.svg',
  '/lib/fontawesome/css/all.min.css',
  '/lib/fontawesome/webfonts/fa-solid-900.woff2',
  // Brand type. latin-ext carries the macron vowels in Māori farm and place names, so it is as
  // load-bearing as latin — see the @font-face rules in site.css.
  '/lib/fonts/montserrat-latin.woff2',
  '/lib/fonts/montserrat-latin-ext.woff2',
  '/lib/fonts/opensans-latin.woff2',
  '/lib/fonts/opensans-latin-ext.woff2'
];

// Served when a page navigation fails offline. Generated here rather than precached because page
// HTML is deliberately never cached: tester pages embed farm details (names, addresses, farmer
// contacts) and the Cache API is shared by every account that uses the device.
const OFFLINE_HTML = `<!DOCTYPE html>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Offline &middot; NZMPTA AutoRep</title>
<style>
  /* Open Sans is precached and self-hosted, so this actually resolves offline now. */
  @font-face { font-family:'Open Sans'; font-style:normal; font-weight:400 700; font-display:swap;
    src:url('/lib/fonts/opensans-latin.woff2') format('woff2'); }
  body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center;
    background:#EEF3FA; color:#333333;
    font-family:'Open Sans',-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; }
  .card { max-width:26rem; margin:1.5rem; padding:2rem; background:#fff; border-radius:10px;
    box-shadow:0 6px 18px rgba(0,0,0,.08); text-align:center; }
  h1 { margin:0 0 .75rem; font-size:1.25rem; color:#003893; }
  p { margin:0 0 1.25rem; line-height:1.5; }
  button { font:inherit; padding:.6rem 1.25rem; border:0; border-radius:6px;
    background:#003893; color:#fff; cursor:pointer; }
</style>
<div class="card">
  <h1>You&rsquo;re offline</h1>
  <p>This page needs a connection. A test you already have open keeps working and saves to this
     device &mdash; reconnect and sync to send it in.</p>
  <button onclick="location.reload()">Try again</button>
</div>`;

function offlineResponse() {
  return new Response(OFFLINE_HTML, {
    status: 503,
    headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }
  });
}

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then(async (cache) => {
      // Per URL rather than cache.addAll, which is atomic: one renamed or half-deployed asset
      // would silently leave the ENTIRE shell uncached, and the old code swallowed that.
      const results = await Promise.allSettled(APP_SHELL.map((asset) => cache.add(asset)));
      const failed = APP_SHELL.filter((_, i) => results[i].status === 'rejected');
      if (failed.length) console.warn('[sw] app shell entries failed to precache:', failed);
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(
        keys.filter((k) => k !== CACHE_VERSION && k !== LOGO_CACHE && k !== FA_CACHE).map((k) => caches.delete(k))
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

  // Font Awesome Pro kit (kit.fontawesome.com + its ka-f.fontawesome.com assets): stale-while-
  // revalidate so the Pro icons keep rendering offline once the kit has loaded online once.
  if (url.hostname.endsWith('.fontawesome.com')) {
    event.respondWith(
      caches.open(FA_CACHE).then((cache) =>
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

  // API, auth flows + the health probe: always go to the network (never cache-serve, so the
  // connectivity check reflects real reachability).
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/Account/') || url.pathname === '/health') {
    return;
  }

  // Navigation requests: network-first, falling back to the generated offline page. Nothing is
  // read from the cache here on purpose (see OFFLINE_HTML) — and the fallback must be a real
  // Response, since respondWith resolving to anything else surfaces the browser's raw network
  // error instead of a page.
  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request).catch(() => offlineResponse()));
    return;
  }

  // Same-origin static assets: cache-first, populating the cache on first fetch so the app
  // bundle and lazy chunks (e.g. the PDF generator) keep working offline.
  //
  // ignoreSearch so a precached '/css/site.css' still answers a '?v=' request — without it the
  // precache was dead weight and each asset sat in the cache twice. This is only safe because
  // CACHE_VERSION is stamped at build time from the bundle AND the contents of every APP_SHELL
  // asset: a cache-first match that ignores the query string would otherwise pin a device to
  // whatever build it cached first, forever.
  if (url.origin === self.location.origin) {
    event.respondWith(
      caches.match(event.request, { ignoreSearch: true }).then((cached) =>
        cached ||
        fetch(event.request)
          .then((resp) => {
            // A 404 on a fingerprinted asset means a deploy replaced it while this client was
            // still running the old document. Fetch the new worker rather than leaving the tab
            // pointing at a build whose chunks no longer exist.
            if (resp && resp.status === 404) void self.registration.update();
            if (resp && resp.ok && (resp.type === 'basic' || resp.type === 'default')) {
              const clone = resp.clone();
              caches.open(CACHE_VERSION).then((cache) => cache.put(event.request, clone));
            }
            return resp;
          })
          // Offline and never cached. An empty body made a module script fail with nothing to
          // act on — a blank page and a silent console; say which asset is missing instead.
          .catch(() => new Response(
            `/* offline: ${url.pathname} has never been cached on this device */`,
            {
              status: 504,
              statusText: 'Offline and not cached',
              headers: { 'Content-Type': 'text/plain; charset=utf-8' }
            }
          ))
      )
    );
  }
});
