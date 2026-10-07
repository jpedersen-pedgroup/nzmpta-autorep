// Registers the service worker so the application shell is cached for offline use, and tells the
// tester when a new build has taken over.
//
// sw.js activates a new build the moment it has installed (skipWaiting + clients.claim) and retires
// the previous cache with it, so the page that noticed the update is left running code whose lazy
// chunks (the PDF generator, for one) no longer exist anywhere. Nothing is lost — every edit is
// already in IndexedDB — but the next thing to do is reload, and the tester has to be told so.
// Without this, "did the fix reach my device?" has no visible answer: the first screen after a
// deploy is always the old build.
if ('serviceWorker' in navigator) {
  // Only a page whose device already had a worker can be "updated". A device's first install also
  // fires controllerchange (no controller → controller) and must stay silent. The registration,
  // not the controller, is the evidence: a force-refreshed page is uncontrolled yet still has one.
  let hadWorker = Boolean(navigator.serviceWorker.controller);
  navigator.serviceWorker.getRegistration().then((reg) => {
    if (reg && (reg.active || reg.waiting)) hadWorker = true;
  });

  // The sign-in and finish-sync pages sit outside the worker's fetch handling, and the next thing
  // the user does there is a full navigation, which already lands on the new build. A banner
  // there would only invite re-submitting a failed sign-in.
  const quietPage = location.pathname.startsWith('/Account/');

  const onNewBuild = () => {
    if (hadWorker && !quietPage) showUpdateBanner();
  };
  navigator.serviceWorker.addEventListener('controllerchange', onNewBuild);
  // A navigation-triggered update can finish before this script runs, so controllerchange has
  // already been and gone. sw.js also posts a message from activate, and the browser queues that
  // until the page starts listening.
  navigator.serviceWorker.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'sw-activated') onNewBuild();
  });
  navigator.serviceWorker.startMessages();

  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js')
      .catch((err) => console.warn('SW registration failed:', err));
  });
}

let updateBannerDismissed = false;

function showUpdateBanner() {
  if (updateBannerDismissed || document.getElementById('sw-update')) return;
  const bar = document.createElement('div');
  bar.id = 'sw-update';
  bar.className = 'sw-update';
  bar.setAttribute('role', 'status');

  const text = document.createElement('span');
  text.textContent = 'AutoRep has been updated.';

  const reload = document.createElement('button');
  reload.type = 'button';
  reload.className = 'btn btn--sm sw-update__btn';
  reload.textContent = 'Reload to get the latest';
  // A GET of the current URL rather than reload(): a page that came from a form POST (a failed
  // sign-in, an admin save) must not be re-submitted.
  reload.addEventListener('click', () => location.replace(location.href));

  const later = document.createElement('button');
  later.type = 'button';
  later.className = 'btn btn--sm sw-update__later';
  later.textContent = 'Later';
  later.addEventListener('click', () => {
    updateBannerDismissed = true;
    bar.remove();
  });

  bar.append(text, reload, later);
  // Keep clear of the sticky Previous/Next bar the scroll and hub wizard layouts pin to the bottom.
  const foot = document.querySelector('.wizard-shell__bar-foot');
  if (foot) bar.style.bottom = `${Math.round(foot.getBoundingClientRect().height) + 12}px`;
  document.body.appendChild(bar);
}

// ---- Signing out ---------------------------------------------------------------------------------
// Plain script on purpose: this file loads on every page (the header's Sign out form is on all of
// them, the bundle is not), and in the offline shell. Before the sign-out form goes to the server:
//  1. Warn when this tester still has tests only on this device. A warning, never a block: a
//     blocked sign-out teaches "clear website data" as the workaround, and THAT destroys the work.
//     Signing out keeps the tests — they send at the tester's next sign-in and sync.
//  2. Forget who was signed in. The identity record (Client/db/identity.ts) is what the offline
//     shell opens tests for, and the next person to pick up the iPad must not land in this
//     tester's tests with no signal. The shell's Sign out is a link to /Account/Logout, whose
//     confirmation page posts this same form, so both paths come through here.
var IDENTITY_DB = 'autorep-identity';

function settleWithin(promise, ms, fallback) {
  return Promise.race([promise, new Promise(function (resolve) { setTimeout(function () { resolve(fallback); }, ms); })]);
}

// Tests the server hasn't got, in the signed-in tester's database; null when unknown.
function countUnsyncedTests() {
  var testerId = window.__autorepTesterId;
  if (typeof testerId !== 'string' || !testerId || !('indexedDB' in window)) return Promise.resolve(null);
  return new Promise(function (resolve) {
    var created = false;
    var req;
    try { req = indexedDB.open('autorep_' + testerId); } catch (e) { resolve(null); return; }
    // No database means no tests — don't create an empty one just by looking.
    req.onupgradeneeded = function () { created = true; req.transaction.abort(); };
    req.onerror = function () { resolve(created ? 0 : null); };
    req.onblocked = function () { resolve(null); };
    req.onsuccess = function () {
      var db = req.result;
      try {
        if (!db.objectStoreNames.contains('tests')) { db.close(); resolve(0); return; }
        var all = db.transaction('tests').objectStore('tests').getAll();
        all.onsuccess = function () {
          db.close();
          resolve(all.result.filter(function (t) { return t && t.syncState === 'local-only'; }).length);
        };
        all.onerror = function () { db.close(); resolve(null); };
      } catch (e) { db.close(); resolve(null); }
    };
  });
}

function forgetIdentity() {
  return new Promise(function (resolve) {
    try {
      var req = indexedDB.deleteDatabase(IDENTITY_DB);
      req.onsuccess = req.onerror = req.onblocked = function () { resolve(); };
    } catch (e) { resolve(); }
  });
}

document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post') return;
  var path;
  try { path = new URL(form.action, location.href).pathname; } catch (e) { return; }
  if (!/^\/Account\/Logout\/?$/i.test(path)) return;
  event.preventDefault();
  settleWithin(countUnsyncedTests(), 1500, null).then(function (unsynced) {
    if (unsynced > 0 && !window.confirm(
      unsynced + ' test' + (unsynced === 1 ? ' is' : 's are') + ' still only on this device. ' +
      (unsynced === 1 ? 'It stays' : 'They stay') + ' saved here and will send the next time you sign in and sync.\n\nSign out anyway?')) {
      return;
    }
    // form.submit() doesn't fire 'submit' again, so this handler can't loop.
    return settleWithin(forgetIdentity(), 1500, undefined).then(function () { form.submit(); });
  });
});

// ---- A bundle that can't load ----------------------------------------------------------------------
// The service worker serves the app bundle cache-first, and a deploy replaces it. If a device ever
// holds a bundle whose chunks the server no longer has (and the cache doesn't either), the module
// graph fails and the page is blank — and reloading serves the same broken copy again. Recover once:
// ask for the new worker (a new build precaches a complete bundle of its own and takes over), or,
// if this IS the current worker, drop its copies of the bundle so the reload fetches them fresh.
// Never while the network is down — that would throw away the only copy — and never more than
// once a minute, so a server that is itself broken can't put the tablet in a reload loop.
var RECOVERY_KEY = 'autorep:bundle-recovery';

function showAppProblem(text) {
  if (document.getElementById('app-load-problem')) return;
  var bar = document.createElement('div');
  bar.id = 'app-load-problem';
  bar.className = 'sw-update';
  bar.setAttribute('role', 'alert');
  var span = document.createElement('span');
  span.textContent = text;
  var retry = document.createElement('button');
  retry.type = 'button';
  retry.className = 'btn btn--sm sw-update__btn';
  retry.textContent = 'Try again';
  retry.addEventListener('click', function () { location.replace(location.href); });
  bar.append(span, retry);
  (document.body || document.documentElement).appendChild(bar);
}

function dropCachedBundle() {
  return caches.keys().then(function (names) {
    return Promise.all(names.filter(function (n) { return n.indexOf('autorep-') === 0; }).map(function (name) {
      return caches.open(name).then(function (cache) {
        return cache.keys().then(function (requests) {
          return Promise.all(requests
            .filter(function (r) { return new URL(r.url).pathname.indexOf('/js/dist/') === 0; })
            .map(function (r) { return cache.delete(r); }));
        });
      });
    }));
  });
}

function recoverBundle() {
  var last = 0;
  try { last = Number(sessionStorage.getItem(RECOVERY_KEY)) || 0; } catch (e) { last = Date.now(); }
  if (Date.now() - last < 60000 || !('serviceWorker' in navigator)) {
    showAppProblem('AutoRep couldn’t finish loading. Check your connection, then try again.');
    return;
  }
  navigator.serviceWorker.getRegistration().then(function (reg) {
    if (!reg) { location.replace(location.href); return; }
    // update() fetches sw.js from the server, so it also tells us whether the network is really up.
    return reg.update().then(function () {
      try { sessionStorage.setItem(RECOVERY_KEY, String(Date.now())); } catch (e) { /* one try either way */ }
      if (reg.installing || reg.waiting) {
        // A new build is installing and brings a complete bundle of its own. Reload once it's in.
        return settleWithin(new Promise(function (resolve) {
          navigator.serviceWorker.addEventListener('controllerchange', resolve);
        }), 20000, undefined).then(function () { location.replace(location.href); });
      }
      return dropCachedBundle().then(function () { location.replace(location.href); });
    }, function () {
      showAppProblem('AutoRep couldn’t finish loading and there’s no connection to repair it. Reconnect, then try again.');
    });
  }).catch(function () {
    showAppProblem('AutoRep couldn’t finish loading. Check your connection, then try again.');
  });
}

// A <script> that fails to load fires 'error' on the element, which doesn't bubble, so capture it.
// For a module script that includes any chunk in its import graph failing to fetch.
window.addEventListener('error', function (event) {
  var el = event.target;
  if (!(el instanceof HTMLScriptElement) || !el.src) return;
  if (new URL(el.src, location.href).pathname.indexOf('/js/dist/') !== 0) return;
  recoverBundle();
}, true);
