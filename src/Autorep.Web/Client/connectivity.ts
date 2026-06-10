// Real connectivity: whether the server's /health endpoint is actually reachable, not just
// whether the device is on a network (navigator.onLine can be true with no real internet).
import { useEffect, useState } from "preact/hooks";

const HEALTH_URL = "/health";
const POLL_MS = 30_000; // re-check every 30s
const TIMEOUT_MS = 5_000; // treat a slow/hung request as offline

export async function isServerReachable(): Promise<boolean> {
  if (!navigator.onLine) return false; // definitely offline — skip the request
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(HEALTH_URL, { method: "GET", cache: "no-store", signal: controller.signal });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Tracks whether the server is reachable. Re-checks: on mount, every 30s, on the browser's
 * online/offline events, and whenever the tab regains focus.
 */
export function useServerOnline(): boolean {
  const [online, setOnline] = useState(navigator.onLine);

  useEffect(() => {
    let cancelled = false;
    const check = () => {
      void isServerReachable().then((ok) => {
        if (!cancelled) setOnline(ok);
      });
    };

    check();
    const interval = setInterval(check, POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") check();
    };
    addEventListener("online", check);
    addEventListener("offline", check);
    document.addEventListener("visibilitychange", onVisible);

    return () => {
      cancelled = true;
      clearInterval(interval);
      removeEventListener("online", check);
      removeEventListener("offline", check);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  return online;
}
