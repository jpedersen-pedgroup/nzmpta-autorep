// The New-test page (/App/Tests/New): choose a farm from the farm book cached on this device and
// open the wizard, which creates the test on the device. Works with no signal — the offline shell
// serves this same page — because nothing here needs the server except ADDING a farm, which is
// online-only by decision (plans/offline-tester-app.md, Phase 5 cut) and says so plainly.
//
// Used to be a Razor page that inlined the company's whole farm book and POSTed the choice to the
// server for a redirect. The scope check that POST did still happens where data crosses: the
// wizard's GET /api/farms/{id} and the sync push. The picker only ever offers the tester's own,
// already-scoped book.
import { render } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import { addFarmToCache, getCachedFarms, milkCompanyLogoUrl, type CachedFarm } from "../sync/farmsSync";
import { REFERENCE_REFRESHED_EVENT } from "../appEvents";
import { fetchWithTimeout, isServerReachable, reportSignedOut } from "../connectivity";
import { getPrivacyContent } from "../config/privacyContent";

export function mountNewTest(root: HTMLElement): void {
  render(<NewTestApp />, root);
}

/** Where "Start test" goes: the wizard, given the farm. */
export function wizardUrlFor(farm: Pick<CachedFarm, "id" | "name">): string {
  return `/App/Tests/Wizard?${new URLSearchParams({ farmId: farm.id, farmName: farm.name })}`;
}

/** The picker's list: farms whose name, supply number or town contains what was typed. */
export function matchFarms(farms: readonly CachedFarm[], query: string, limit = 50): CachedFarm[] {
  const q = query.trim().toLowerCase();
  const hits = q === "" ? farms : farms.filter((f) => [f.name, f.supplyNumber, f.town].some((v) => v?.toLowerCase().includes(q)));
  return hits.slice(0, limit);
}

/** The forgiving start: a typed name that matches exactly one farm counts as choosing it. Two
 * farms with the same name is a real case (different regions), so then nothing is guessed. */
export function exactFarm(farms: readonly CachedFarm[], typed: string): CachedFarm | null {
  const q = typed.trim().toLowerCase();
  if (!q) return null;
  const matches = farms.filter((f) => f.name.trim().toLowerCase() === q);
  return matches.length === 1 ? matches[0] : null;
}

/** What tells two same-named farms apart in the list. */
export function farmDetail(f: CachedFarm): string {
  return [f.supplyNumber ? `Supply ${f.supplyNumber}` : null, f.town, f.milkCompanyName].filter(Boolean).join(" · ");
}

const byName = (a: CachedFarm, b: CachedFarm) => a.name.localeCompare(b.name);

function NewTestApp() {
  const [farms, setFarms] = useState<CachedFarm[] | null>(null);
  const [query, setQuery] = useState("");
  const [chosen, setChosen] = useState<CachedFarm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    let active = true;
    // Read once on mount, and again when the background refresh brings a newer book.
    const load = () => {
      void getCachedFarms().then((list) => {
        if (active) setFarms([...list].sort(byName));
      });
    };
    load();
    addEventListener(REFERENCE_REFRESHED_EVENT, load);
    return () => {
      active = false;
      removeEventListener(REFERENCE_REFRESHED_EVENT, load);
    };
  }, []);

  const start = () => {
    const farm = chosen ?? exactFarm(farms ?? [], query);
    if (!farm) {
      setError("Choose a farm from the list, or add a new one.");
      return;
    }
    location.href = wizardUrlFor(farm);
  };

  const empty = farms !== null && farms.length === 0;
  return (
    <>
      <div class="page-header">
        <div class="page-header__heading">
          <h1>New machine test</h1>
          <p>Choose the farm, or add a new one.</p>
        </div>
      </div>

      <div class="card" style="max-width:640px">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            start();
          }}
        >
          <div class="form-field">
            <label for="farm-search">Farm</label>
            <FarmPicker
              farms={farms ?? []}
              query={query}
              onQuery={(q) => {
                setQuery(q);
                setChosen(null);
                setError(null);
              }}
              onPick={(f) => {
                setChosen(f);
                setQuery(f.name);
                setError(null);
              }}
            />
            <p class="form-field__hint">
              {empty
                ? "No farms on this device yet. Connect once and your company's farm book downloads, or add a new farm (needs signal)."
                : "Your company's farms, saved on this device — this works with no signal. Not listed? Add a new one."}
            </p>
            <div style="margin-top:var(--space-3)">
              <button type="button" class="btn btn--add" onClick={() => setAdding(true)}>
                ＋ Add a new farm
              </button>
            </div>
            {chosen?.milkCompanyId && <MilkCompanyLogo id={chosen.milkCompanyId} name={chosen.milkCompanyName} />}
          </div>

          {error && <div class="alert alert--danger">{error}</div>}
          <div class="form-actions">
            <button type="submit" class="btn">
              Start test
            </button>
            <a class="btn btn--secondary" href="/App/Tests">
              Cancel
            </a>
          </div>
        </form>
      </div>

      {adding && (
        <AddFarmModal
          onClose={() => setAdding(false)}
          onCreated={(farm) => {
            setFarms((list) => [...(list ?? []).filter((f) => f.id !== farm.id), farm].sort(byName));
            setChosen(farm);
            setQuery(farm.name);
            setError(null);
            setAdding(false);
          }}
        />
      )}
    </>
  );
}

function FarmPicker({
  farms,
  query,
  onQuery,
  onPick,
}: {
  farms: readonly CachedFarm[];
  query: string;
  onQuery: (q: string) => void;
  onPick: (farm: CachedFarm) => void;
}) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const wrapRef = useRef<HTMLDivElement>(null);
  const list = open ? matchFarms(farms, query) : [];

  useEffect(() => {
    if (!open) return;
    const outside = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", outside);
    return () => document.removeEventListener("mousedown", outside);
  }, [open]);

  const pick = (farm: CachedFarm) => {
    onPick(farm);
    setOpen(false);
    setActive(-1);
  };

  return (
    <div class="autocomplete" ref={wrapRef}>
      <input
        id="farm-search"
        type="text"
        role="combobox"
        autocomplete="off"
        aria-expanded={open}
        aria-controls="farm-menu"
        aria-autocomplete="list"
        aria-activedescendant={open && active >= 0 ? `farm-opt-${active}` : undefined}
        placeholder="Type to search your farms…"
        value={query}
        onFocus={() => setOpen(true)}
        onInput={(e) => {
          onQuery((e.currentTarget as HTMLInputElement).value);
          setOpen(true);
          setActive(-1);
        }}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            setOpen(true);
            setActive((a) => Math.min(a + 1, matchFarms(farms, query).length - 1));
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            setActive((a) => Math.max(a - 1, 0));
          } else if (e.key === "Enter" && open && active >= 0 && list[active]) {
            e.preventDefault(); // pick, don't submit
            pick(list[active]);
          } else if (e.key === "Escape") {
            setOpen(false);
          }
        }}
      />
      {open && (
        <div id="farm-menu" class="autocomplete-menu" role="listbox">
          {list.length === 0 ? (
            <div class="autocomplete-menu__empty">
              {farms.length === 0 ? "No farms on this device yet." : "No matching farms — add a new one below."}
            </div>
          ) : (
            list.map((f, i) => {
              const detail = farmDetail(f);
              return (
                <button
                  key={f.id}
                  id={`farm-opt-${i}`}
                  type="button"
                  role="option"
                  aria-selected={i === active}
                  class={i === active ? "is-active" : undefined}
                  // mousedown, not click: a click would blur the input first and close the list.
                  onMouseDown={(e) => {
                    e.preventDefault();
                    pick(f);
                  }}
                >
                  {f.name}
                  {detail && (
                    <span class="td-muted" style="display:block;font-size:0.8125rem">
                      {detail}
                    </span>
                  )}
                </button>
              );
            })
          )}
        </div>
      )}
    </div>
  );
}

/** Offline it comes from the service worker's logo cache, warmed after each farm-book sync. */
function MilkCompanyLogo({ id, name }: { id: string; name?: string | null }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [id]);
  if (failed) return null;
  return (
    <div style="margin-top:var(--space-3)">
      <span style="font-size:0.8125rem;color:var(--text-muted)">Milk supply company</span>
      <img
        src={milkCompanyLogoUrl(id)}
        alt={name ? `${name} logo` : "Milk supply company logo"}
        style="display:block;max-height:48px;max-width:160px;margin-top:4px"
        onError={() => setFailed(true)}
      />
    </div>
  );
}

interface RegionOption {
  id: string;
  name: string;
  island: string;
}
interface MilkCompanyOption {
  id: string;
  name: string;
}
interface NewFarmOptions {
  regions: RegionOption[];
  milkCompanies: MilkCompanyOption[];
}

type OptionsResult = { kind: "ok"; options: NewFarmOptions } | { kind: "offline" } | { kind: "signed-out" };

async function loadNewFarmOptions(): Promise<OptionsResult> {
  if (!(await isServerReachable())) return { kind: "offline" };
  try {
    const res = await fetchWithTimeout("/api/farms/new-farm-options", {
      headers: { Accept: "application/json" },
      redirect: "manual",
    });
    if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect") {
      reportSignedOut();
      return { kind: "signed-out" };
    }
    if (!res.ok) return { kind: "offline" };
    const options = (await res.json()) as NewFarmOptions;
    return Array.isArray(options?.regions) && Array.isArray(options?.milkCompanies)
      ? { kind: "ok", options }
      : { kind: "offline" };
  } catch {
    return { kind: "offline" };
  }
}

const FIELDS = [
  ["addressLine1", "Address line 1", "nf-addr1", true],
  ["addressLine2", "Address line 2", "nf-addr2", true],
  ["town", "Town / city", "nf-town", false],
  ["postCode", "Post code", "nf-postcode", false],
  ["rapidNumber", "RAPID number", "nf-rapid", false],
  ["farmerName", "Farmer name", "nf-farmer", false],
  ["contactPhone", "Phone", "nf-phone", false],
  ["contactEmail", "Email", "nf-email", false],
] as const;

function AddFarmModal({ onClose, onCreated }: { onClose: () => void; onCreated: (farm: CachedFarm) => void }) {
  // One id for the farm this form adds, sent with every attempt: if an answer is lost, trying
  // again returns the farm already saved rather than adding it twice (FarmsController.Create).
  const [farmId] = useState(() => crypto.randomUUID());
  const [state, setState] = useState<OptionsResult | { kind: "checking" }>({ kind: "checking" });
  const [attempt, setAttempt] = useState(0);
  const [values, setValues] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let active = true;
    setState({ kind: "checking" });
    void loadNewFarmOptions().then((result) => {
      if (active) setState(result);
    });
    return () => {
      active = false;
    };
  }, [attempt]);

  const set = (key: string) => (e: Event) => setValues((v) => ({ ...v, [key]: (e.currentTarget as HTMLInputElement).value }));
  const value = (key: string) => {
    const v = (values[key] ?? "").trim();
    return v === "" ? null : v;
  };

  const create = async () => {
    setError(null);
    if (!value("name")) {
      setError("Farm name is required.");
      return;
    }
    setSaving(true);
    try {
      const res = await fetchWithTimeout(
        "/api/farms",
        {
          method: "POST",
          redirect: "manual",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({
            id: farmId,
            name: value("name"),
            supplyNumber: value("supplyNumber"),
            milkSupplyCompanyId: value("milkSupplyCompanyId"),
            regionId: value("regionId"),
            addressLine1: value("addressLine1"),
            addressLine2: value("addressLine2"),
            town: value("town"),
            postCode: value("postCode"),
            rapidNumber: value("rapidNumber"),
            farmerName: value("farmerName"),
            contactPhone: value("contactPhone"),
            contactEmail: value("contactEmail"),
          }),
        },
        15_000,
      );
      if (res.status === 401 || res.type === "opaqueredirect") {
        reportSignedOut();
        setError("You've been signed out, so the farm wasn't added. Sign in again, then add it.");
        return;
      }
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { error?: string } | null;
        setError(body?.error ?? `The server couldn't add the farm (${res.status}). Try again.`);
        return;
      }
      const farm = (await res.json()) as CachedFarm;
      await addFarmToCache(farm).catch(() => undefined);
      onCreated(farm);
    } catch {
      // The request may have reached the server and been saved before the answer was lost, so
      // don't claim nothing was saved. Trying again is safe either way: same farm id.
      setError(
        navigator.onLine === false
          ? "You're offline — adding a farm needs signal. Try again when you're back in range."
          : "The connection dropped before the server answered, so the farm may or may not have been added. " +
              "Try again — it won't be added twice.",
      );
    } finally {
      setSaving(false);
    }
  };

  const notice = getPrivacyContent().collectionNotice;
  const islands = state.kind === "ok" ? [...new Set(state.options.regions.map((r) => r.island || "Other"))] : [];

  return (
    <div
      class="modal-overlay open"
      id="farm-modal"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div class="modal modal--wide" role="dialog" aria-modal="true" aria-labelledby="farm-modal-title">
        {state.kind === "checking" && (
          <>
            <div class="modal__title" id="farm-modal-title">
              Add a new farm
            </div>
            <p class="td-muted">Checking the connection…</p>
          </>
        )}

        {(state.kind === "offline" || state.kind === "signed-out") && (
          <>
            <div class="modal__title" id="farm-modal-title">
              {state.kind === "offline" ? "Adding a farm needs a connection" : "Sign in again to add a farm"}
            </div>
            <p>
              {state.kind === "offline"
                ? "New farms are set up online, so the office knows about them straight away. You can start a test on any farm already on this device — or add this one when you're back in range."
                : "Your sign-in has expired. Your tests are safe on this device; sign in again, then add the farm."}
            </p>
            <div class="form-actions">
              {state.kind === "offline" ? (
                <button type="button" class="btn" onClick={() => setAttempt((n) => n + 1)}>
                  Try again
                </button>
              ) : (
                <a class="btn" href={`/Account/Login?ReturnUrl=${encodeURIComponent("/App/Tests/New")}`}>
                  Sign in
                </a>
              )}
              <button type="button" class="btn btn--secondary" onClick={onClose}>
                Close
              </button>
            </div>
          </>
        )}

        {state.kind === "ok" && (
          <>
            <div class="modal__title" id="farm-modal-title">
              Add a new farm
            </div>
            <div id="farm-modal-errors">{error && <div class="alert alert--danger">{error}</div>}</div>
            <div class="form-grid">
              <div class="form-field form-field--full">
                <label for="nf-name">Farm name</label>
                <input id="nf-name" type="text" value={values.name ?? ""} onInput={set("name")} />
              </div>
              <div class="form-field">
                <label for="nf-supply">Supply number</label>
                <input id="nf-supply" type="text" value={values.supplyNumber ?? ""} onInput={set("supplyNumber")} />
              </div>
              <div class="form-field">
                <label for="nf-milkco">Milk supply company</label>
                <select id="nf-milkco" value={values.milkSupplyCompanyId ?? ""} onChange={set("milkSupplyCompanyId")}>
                  <option value="">— Not set —</option>
                  {state.options.milkCompanies.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>
              <div class="form-field form-field--full">
                <label for="nf-region">Region</label>
                <select id="nf-region" value={values.regionId ?? ""} onChange={set("regionId")}>
                  <option value="">— Not set —</option>
                  {islands.map((island) => (
                    <optgroup key={island} label={island}>
                      {state.options.regions
                        .filter((r) => (r.island || "Other") === island)
                        .map((r) => (
                          <option key={r.id} value={r.id}>
                            {r.name}
                          </option>
                        ))}
                    </optgroup>
                  ))}
                </select>
              </div>
              {FIELDS.map(([key, label, id, full]) => (
                <div key={key} class={`form-field${full ? " form-field--full" : ""}`}>
                  <label for={id}>{label}</label>
                  <input id={id} type={key === "contactEmail" ? "email" : "text"} value={values[key] ?? ""} onInput={set(key)} />
                </div>
              ))}
            </div>
            {notice && (
              <p class="privacy-notice">
                <i class="fa-solid fa-shield-halved" aria-hidden="true"></i> {notice}
              </p>
            )}
            <p class="form-field__hint">
              New farms are flagged for your Company Administrator to review — you can start testing straight away.
            </p>
            <div class="form-actions">
              <button type="button" class="btn" id="nf-create" disabled={saving} onClick={() => void create()}>
                {saving ? "Adding…" : "Add farm"}
              </button>
              <button type="button" class="btn btn--secondary" onClick={onClose}>
                Cancel
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
