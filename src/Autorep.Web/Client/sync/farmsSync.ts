// Syncs the caller's farm book (company-scoped, from /api/farms) to the device on every tester
// page, mirroring the cache-then-fresh reference-data pattern (standardsSync et al). The cache is
// what the New-test picker offers and what the wizard snapshots a farm from while offline —
// including farms the office added since the device last loaded the app. Full replace on every
// changed answer: farms deactivated or re-scoped since the last visit drop out of the cache rather
// than lingering.
//
// The book is the largest thing a tester page fetches, so it is fetched conditionally: the server
// stamps an ETag over the exact bytes, the device keeps it with the rows, and an unchanged book
// costs a bodyless 304.
//
// A farm added with no signal waits here too, in a queue of its own (one reference entry per farm,
// so the book's full replace can't touch it). It shows in the book straight away, so a test can start
// on it, and goes up through the same idempotent POST /api/farms the add-a-farm form uses, ahead of
// the tests (syncClient.ts). A test on a farm still in the queue waits for it: sent first, it would
// land on the sync's safety net, which mints a farm of its own with no address or contact details.
import {
  deleteReference,
  getReference,
  putReference,
  QUEUED_FARM_KEY_PREFIX,
  referencesWithPrefix,
} from "../db/testStore";
import { fetchWithTimeout, reportSignedOut } from "../connectivity";

/** A farm as cached on-device (mirrors the server FarmDto). */
export interface CachedFarm {
  id: string;
  name: string;
  supplyNumber?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  town?: string | null;
  postCode?: string | null;
  rapidNumber?: string | null;
  regionName?: string | null;
  milkCompanyName?: string | null;
  farmerName?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  milkCompanyId?: string | null;
  /** Added on this device and not on the server yet. Never comes from the server. */
  notSent?: boolean;
}

/** The add-a-farm form as it goes to POST /api/farms (FarmsController.NewFarmRequest). */
export interface NewFarmRequest {
  id: string;
  name: string | null;
  supplyNumber: string | null;
  milkSupplyCompanyId: string | null;
  regionId: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  town: string | null;
  postCode: string | null;
  rapidNumber: string | null;
  farmerName: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
}

/** A farm waiting to go up: what to send, and how the book shows it until then. */
export interface QueuedFarm {
  request: NewFarmRequest;
  farm: CachedFarm;
  queuedAt: string;
  /** Why the server wouldn't take it last time. It's tried again on every sync. */
  problem?: string | null;
}

export interface RegionOption {
  id: string;
  name: string;
  island: string;
}
export interface MilkCompanyOption {
  id: string;
  name: string;
}
/** The add-a-farm form's two pick lists (FarmsController.NewFarmOptions). */
export interface NewFarmOptions {
  regions: RegionOption[];
  milkCompanies: MilkCompanyOption[];
}

const REF_KEY = "farms";
const OPTIONS_KEY = "newFarmOptions";

/** A milk-supply company's logo. The service worker keeps every one it fetches (LOGO_CACHE). */
export function milkCompanyLogoUrl(milkCompanyId: string): string {
  return `/api/milk-companies/${milkCompanyId}/logo`;
}

/** Refreshes the cached farm book. True when it changed (and is now in effect). Offline, signed
 * out or any failure leaves the cached book exactly as it was. */
export async function initFarms(): Promise<boolean> {
  try {
    const cached = await getReference(REF_KEY).catch(() => undefined);
    const headers: Record<string, string> = { Accept: "application/json" };
    if (cached?.version && Array.isArray(cached.rows)) headers["If-None-Match"] = cached.version;

    const res = await fetchWithTimeout("/api/farms", { headers });
    if (res.status === 304) {
      if (firstWarmThisSession()) void warmMilkCompanyLogos(cached?.rows as CachedFarm[]);
      return false;
    }
    if (!res.ok) return false; // offline / unauthenticated — the cached farm book stays in effect
    const farms = (await res.json()) as CachedFarm[];
    if (!Array.isArray(farms)) return false;
    await putReference({ key: REF_KEY, version: res.headers.get("ETag"), rows: farms });
    void warmMilkCompanyLogos(farms);
    return true;
  } catch {
    // Offline — the cached farm book stays in effect.
    return false;
  }
}

/** The last-synced farm book (empty when never synced on this device), plus the farms this device
 * has added and not sent yet. */
export async function getCachedFarms(): Promise<CachedFarm[]> {
  let book: CachedFarm[] = [];
  try {
    const cached = await getReference(REF_KEY);
    if (Array.isArray(cached?.rows)) book = cached.rows as CachedFarm[];
  } catch {
    // Unreadable — the queue below may still answer.
  }
  const known = new Set(book.map((f) => f.id));
  const waiting = (await queuedFarms()).filter((q) => !known.has(q.farm.id));
  return waiting.length > 0 ? [...book, ...waiting.map((q) => ({ ...q.farm, notSent: true }))] : book;
}

/** Offline farm lookup by id against the cached farm book. */
export async function getCachedFarm(id: string): Promise<CachedFarm | undefined> {
  return (await getCachedFarms()).find((f) => f.id === id);
}

/** Puts a farm the tester has just added into the cached book, so it can be picked offline from
 * now on without waiting for the next sync. The stored ETag is left alone: the server's book has
 * changed, so the next refresh gets a full answer and replaces this one anyway. */
export async function addFarmToCache(farm: CachedFarm): Promise<void> {
  const cached = await getReference(REF_KEY).catch(() => undefined);
  const rows = Array.isArray(cached?.rows) ? (cached.rows as CachedFarm[]) : [];
  const next = [...rows.filter((f) => f.id !== farm.id), farm].sort((a, b) => a.name.localeCompare(b.name));
  await putReference({ key: REF_KEY, version: cached?.version ?? null, rows: next });
  void warmMilkCompanyLogos([farm]);
}

/** Refreshes the add-a-farm form's pick lists on the device, so the form works with no signal. They
 * are small and nothing on screen depends on them, so it's a plain fetch with nothing to announce.
 * Offline, signed out or any failure leaves what the device holds. */
export async function initFarmOptions(): Promise<boolean> {
  try {
    const res = await fetchWithTimeout("/api/farms/new-farm-options", {
      headers: { Accept: "application/json" },
      redirect: "manual",
    });
    if (!res.ok || res.type === "opaqueredirect") return false;
    const options = (await res.json()) as NewFarmOptions;
    if (Array.isArray(options?.regions) && Array.isArray(options?.milkCompanies)) {
      await putReference({ key: OPTIONS_KEY, rows: options });
    }
  } catch {
    // Offline — the lists on the device stay in effect.
  }
  return false;
}

/** The pick lists this device holds; null when it has never downloaded them. */
export async function getCachedFarmOptions(): Promise<NewFarmOptions | null> {
  try {
    return ((await getReference(OPTIONS_KEY))?.rows as NewFarmOptions | undefined) ?? null;
  } catch {
    return null;
  }
}

/** Farms waiting to go up, oldest first. Throws when the queue can't be read: the sync must stop
 * rather than take an unreadable queue for an empty one, or a test would go up ahead of its farm. */
export async function readQueuedFarms(): Promise<QueuedFarm[]> {
  const entries = await referencesWithPrefix(QUEUED_FARM_KEY_PREFIX);
  return entries.map((e) => e.rows as QueuedFarm).sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
}

/** The same, for showing: an unreadable queue shows nothing rather than breaking the page. */
export async function queuedFarms(): Promise<QueuedFarm[]> {
  try {
    return await readQueuedFarms();
  } catch {
    return [];
  }
}

/** Keeps a farm on the device until it can be sent, and gives it back as the book will show it. */
export async function queueFarm(request: NewFarmRequest, options: NewFarmOptions | null): Promise<CachedFarm> {
  const milkCompany = options?.milkCompanies.find((c) => c.id === request.milkSupplyCompanyId);
  const farm: CachedFarm = {
    id: request.id,
    name: request.name ?? "",
    supplyNumber: request.supplyNumber,
    addressLine1: request.addressLine1,
    addressLine2: request.addressLine2,
    town: request.town,
    postCode: request.postCode,
    rapidNumber: request.rapidNumber,
    regionName: options?.regions.find((r) => r.id === request.regionId)?.name ?? null,
    milkCompanyName: milkCompany?.name ?? null,
    farmerName: request.farmerName,
    contactPhone: request.contactPhone,
    contactEmail: request.contactEmail,
    milkCompanyId: milkCompany?.id ?? null,
  };
  const queued: QueuedFarm = { request, farm, queuedAt: new Date().toISOString() };
  await putReference({ key: `${QUEUED_FARM_KEY_PREFIX}${request.id}`, rows: queued });
  return { ...farm, notSent: true };
}

/**
 * Sends the farms waiting on this device. Each carries the id the device gave it, so a send whose
 * answer was lost is harmless: the server hands the same farm back rather than adding it twice.
 * Resolves true when every send got an answer other than "you are signed out". No connection ends
 * the run quietly: the farms stay queued, and so do the tests waiting on them.
 */
export async function flushQueuedFarms(): Promise<boolean> {
  for (const queued of await readQueuedFarms()) {
    let res: Response;
    try {
      res = await fetchWithTimeout(
        "/api/farms",
        {
          method: "POST",
          redirect: "manual",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ ...queued.request, queued: true }),
        },
        15_000,
      );
    } catch {
      return true;
    }
    if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect") {
      reportSignedOut();
      return false;
    }
    if (res.ok) {
      const farm = (await res.json()) as CachedFarm;
      await addFarmToCache(farm).catch(() => undefined);
      await deleteReference(`${QUEUED_FARM_KEY_PREFIX}${queued.request.id}`);
    } else {
      const body = (await res.json().catch(() => null)) as { error?: string } | null;
      await putReference({
        key: `${QUEUED_FARM_KEY_PREFIX}${queued.request.id}`,
        rows: { ...queued, problem: body?.error ?? `The server couldn't add it (${res.status}).` } satisfies QueuedFarm,
      });
    }
  }
  return true;
}

const LOGOS_WARMED_KEY = "autorep:logos-warmed";

/** True the first time it's asked in this tab — so an unchanged book still re-checks its logos
 * once a session (a capped or evicted logo cache gets topped up), not on every page. */
function firstWarmThisSession(): boolean {
  try {
    if (sessionStorage.getItem(LOGOS_WARMED_KEY)) return false;
    sessionStorage.setItem(LOGOS_WARMED_KEY, "1");
    return true;
  } catch {
    return false;
  }
}

/**
 * Pre-caches the logo of every milk-supply company in the farm book, so the New-test picker shows
 * one offline for a farm the tester has never chosen before. The service worker's logo rule keeps
 * whatever comes back; this only asks for the ones the device doesn't hold yet. Fire and forget —
 * nobody waits on a logo.
 */
export async function warmMilkCompanyLogos(farms: readonly CachedFarm[] | null | undefined): Promise<number> {
  if (!Array.isArray(farms) || typeof caches === "undefined") return 0;
  const ids = [...new Set(farms.map((f) => f.milkCompanyId).filter((id): id is string => !!id))];
  let fetched = 0;
  for (const id of ids) {
    try {
      const url = milkCompanyLogoUrl(id);
      if (await caches.match(url)) continue;
      const res = await fetchWithTimeout(url);
      if (res.ok) fetched++;
    } catch {
      // Offline part-way through — the rest wait for the next sync.
      break;
    }
  }
  return fetched;
}
