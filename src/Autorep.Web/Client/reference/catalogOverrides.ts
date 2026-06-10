// Admin-managed equipment catalogs, synced from /api/equipment. A synced catalog (per type)
// replaces the bundled default list; with nothing synced the bundled legacy lists apply, so the
// dropdowns work offline and before first sync.
export interface EquipmentDto {
  type: string; // Shell | Liner | Pulsator | MilklineSize | PulsatorConfiguration
  name: string;
  brand?: string | null;
}

let byType: Map<string, EquipmentDto[]> = new Map();

export function applyEquipmentOverrides(items: EquipmentDto[]): void {
  byType = new Map();
  for (const item of items) {
    const list = byType.get(item.type);
    if (list) list.push(item);
    else byType.set(item.type, [item]);
  }
}

/** Test seam / reset. */
export function clearEquipmentOverrides(): void {
  byType = new Map();
}

/** The synced names for a catalog type, or null when nothing is synced (use the bundled list). */
export function catalogNames(type: string): string[] | null {
  const list = byType.get(type);
  return list ? list.map((i) => i.name) : null;
}

/** The synced pulsator models (name + brand), or null when nothing is synced. */
export function catalogPulsators(): { name: string; brand: string }[] | null {
  const list = byType.get("Pulsator");
  return list ? list.map((i) => ({ name: i.name, brand: i.brand ?? "" })) : null;
}
