// Registers the service worker so the application shell is cached for offline use.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js')
      .then((reg) => { /* registered */ })
      .catch((err) => console.warn('SW registration failed:', err));
  });
}
