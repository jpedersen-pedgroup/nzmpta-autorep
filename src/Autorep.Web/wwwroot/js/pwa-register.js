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
