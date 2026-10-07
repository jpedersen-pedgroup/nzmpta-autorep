// The pulsation analyser PDF (O3) is by far the biggest thing a test carries — up to 15 MB, stored
// as base64 inside the test — and iPads evict a whole origin when storage runs short, unsynced
// captures included. So the device keeps an attachment's bytes only while it is likely to print
// it, and lets them go once the server is known to hold them:
//
//  - Attaching (or fetching back) starts a retention clock, `heldSince`.
//  - A test that is complete, synced, and whose bytes have been held for RETAIN_DAYS lets them go:
//    the attachment keeps its name/size/date and becomes a pointer (`onServer`).
//  - The first pull asks the server to leave the bytes behind (attachments=omit), so a new device
//    doesn't download every PDF the tester ever attached.
//  - Printing fetches the bytes back (GET /api/sync/tests/{id}/pulsation-pdf) and holds them again.
//  - Re-sending a test that carries a pointer is safe: the server puts its stored bytes back
//    (SyncController.WithAttachmentBytesAsync), so the device can never be the reason they're lost.
//
// Seven days, not "straight after the push": the common case is sign off, sync, and print for the
// farmer at the gate — possibly after the signal has gone again.
import { getTest, putTest, type LocalTest, type PulsationAttachment } from "../db/testStore";
import { fetchWithTimeout } from "../connectivity";

export const RETAIN_DAYS = 7;
const RETAIN_MS = RETAIN_DAYS * 86_400_000;

/** Same attachment (not just the same file name: analyser software exports under a fixed name). */
export function sameAttachment(a: PulsationAttachment | null | undefined, b: PulsationAttachment | null | undefined): boolean {
  return !!a && !!b && a.name === b.name && a.size === b.size && a.attachedAt === b.attachedAt;
}

/** True when this device should let go of the test's attachment bytes now. */
export function shouldLetGo(t: LocalTest, now: Date): boolean {
  const p = t.pulsationPdf;
  if (!p?.base64) return false;
  // Only once the server demonstrably has this exact version: synced, and not dirty since.
  if (t.syncState !== "uploaded" || !t.everUploaded || !t.markedCompleteAt) return false;
  const since = Date.parse(p.heldSince ?? p.attachedAt);
  return Number.isFinite(since) && now.getTime() - since > RETAIN_MS;
}

/** The attachment as a pointer: everything but the bytes. */
export function withoutBytes(p: PulsationAttachment): PulsationAttachment {
  const { base64: _bytes, heldSince: _held, ...rest } = p;
  return { ...rest, onServer: true };
}

/**
 * Lets go of attachment bytes the device no longer needs to hold (see shouldLetGo). Each test is
 * re-read just before it is written, so an edit that landed meanwhile is never overwritten. Returns
 * how many were let go. Never throws: holding a PDF a little longer is never a problem.
 */
export async function letGoOfHeldAttachments(tests: readonly LocalTest[], now = new Date()): Promise<number> {
  let released = 0;
  for (const t of tests) {
    if (!shouldLetGo(t, now)) continue;
    try {
      const fresh = await getTest(t.id);
      if (!fresh || !shouldLetGo(fresh, now) || !fresh.pulsationPdf) continue;
      await putTest({ ...fresh, pulsationPdf: withoutBytes(fresh.pulsationPdf) });
      released++;
    } catch {
      // Storage trouble — try again next sync.
    }
  }
  return released;
}

/**
 * A pull's copy of a test replaces this device's clean copy — but the server leaves attachment
 * bytes behind, so don't throw away bytes this device still holds for the same attachment.
 */
export function keepHeldBytes(existing: LocalTest | undefined, incoming: LocalTest): LocalTest {
  const held = existing?.pulsationPdf;
  const coming = incoming.pulsationPdf;
  if (!held?.base64 || !coming || coming.base64 || !sameAttachment(held, coming)) return incoming;
  return { ...incoming, pulsationPdf: held };
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(binary);
}

/**
 * The attachment's bytes for a report: held on the device, or fetched back from the server (and
 * held again — a report printed once is often printed again). Null when there is no attachment,
 * or the bytes are on the server and it can't be reached right now.
 */
export async function attachmentBase64(test: LocalTest): Promise<string | null> {
  const p = test.pulsationPdf;
  if (!p) return null;
  if (p.base64) return p.base64;
  if (!p.onServer) return null;
  try {
    const res = await fetchWithTimeout(
      `/api/sync/tests/${encodeURIComponent(p.serverTestId ?? test.id)}/pulsation-pdf`,
      { redirect: "manual" },
      30_000,
    );
    if (!res.ok || res.type === "opaqueredirect") return null;
    const base64 = bytesToBase64(new Uint8Array(await res.arrayBuffer()));
    try {
      const fresh = await getTest(test.id);
      if (fresh?.pulsationPdf && sameAttachment(fresh.pulsationPdf, p) && !fresh.pulsationPdf.base64) {
        const { onServer: _onServer, ...rest } = fresh.pulsationPdf;
        await putTest({ ...fresh, pulsationPdf: { ...rest, base64, heldSince: new Date().toISOString() } });
      }
    } catch {
      // Not kept — the report still gets the bytes this time.
    }
    return base64;
  } catch {
    return null;
  }
}
