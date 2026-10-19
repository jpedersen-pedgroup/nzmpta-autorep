// Admin-managed OEM Specs (the equipment catalogs and their specs), synced from /api/equipment. A
// synced catalog (per type) replaces the bundled default list; with nothing synced the bundled legacy
// lists apply, so the dropdowns work offline and before first sync.
export interface EquipmentDto {
  /** Absent only in a set cached from a server that predates OEM spec editing. */
  id?: string;
  type: string; // Shell | Liner | Pulsator | MilklineSize | PulsatorConfiguration | VacuumPump | ReleaserPump | Regulator
  name: string;
  brand?: string | null;
  /** The OEM spec (Pulsator and VacuumPump items): null when the item has none. Absent - not null -
   * in a set cached from a server that predates specs, which leaves the bundled specs in effect. */
  spec?: Record<string, unknown> | null;
  /** False for a deactivated item: kept for its spec only, never offered in a dropdown. */
  active?: boolean;
}

let byType: Map<string, EquipmentDto[]> = new Map();

/** `items` is the active set; `inactive` the deactivated items the server sends for their specs. */
export function applyEquipmentOverrides(items: EquipmentDto[], inactive: EquipmentDto[] = []): void {
  byType = new Map();
  for (const item of [...items, ...inactive.map((i) => ({ ...i, active: false }))]) {
    const list = byType.get(item.type);
    if (list) list.push(item);
    else byType.set(item.type, [item]);
  }
}

/** Test seam / reset. */
export function clearEquipmentOverrides(): void {
  byType = new Map();
}

const offered = (type: string): EquipmentDto[] | null =>
  byType.get(type)?.filter((i) => i.active !== false) ?? null;

/** The synced names for a catalog type, or null when nothing is synced (use the bundled list). */
export function catalogNames(type: string): string[] | null {
  return offered(type)?.map((i) => i.name) ?? null;
}

/** The synced models of a branded catalog (name + brand), or null when nothing is synced for it.
 * Pulsators, vacuum pumps and releaser pumps all carry the manufacturer in `brand`. */
export function catalogBranded(type: string): { name: string; brand: string }[] | null {
  return offered(type)?.map((i) => ({ name: i.name, brand: i.brand ?? "" })) ?? null;
}

/** The synced pulsator models (name + brand), or null when nothing is synced. */
export function catalogPulsators(): { name: string; brand: string }[] | null {
  return catalogBranded("Pulsator");
}

/** Every synced item of a type that carries specs, deactivated ones included - or null when the
 * synced set has no specs for that type (nothing synced yet, or synced from a server that predates
 * specs), in which case the bundled specs apply. */
export function syncedSpecItems(type: string): EquipmentDto[] | null {
  const list = byType.get(type);
  return list && list.some((i) => i.spec !== undefined) ? list : null;
}

const collapse = (s: string | null | undefined) => (s ?? "").replace(/\s+/g, " ").trim().toLowerCase();

/** The active synced item a tester picked by brand and name, for recording its id - or null when the
 * name was typed by hand (or nothing is synced). Matched loosely, like the dropdowns; with no brand
 * given, only a name no other brand shares. */
export function offeredItem(type: string, brand: string | null | undefined, name: string | null | undefined): EquipmentDto | null {
  if (!name) return null;
  const named = (offered(type) ?? []).filter((i) => i.id && collapse(i.name) === collapse(name));
  if (collapse(brand)) return named.find((i) => collapse(i.brand) === collapse(brand)) ?? null;
  return named.length === 1 ? named[0] : null;
}
