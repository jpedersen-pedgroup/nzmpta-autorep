// Calibration-expiry status logic for the tester's own test equipment. The dates belong to
// the TESTER (their instruments travel with them farm to farm), not to a farm or test.
// Pure calendar-day maths so it is unit-testable: "due" starts 6 weeks before expiry, and an
// instrument is valid through its expiry date itself. Expired equipment warns the tester but
// NEVER blocks starting or completing a test.

/** The tester's three instrument expiry dates (ISO yyyy-mm-dd, null/absent = never recorded). */
export interface CalibrationDates {
  airFlowMeters?: string | null;
  pulsatorTesters?: string | null;
  vacuumGauges?: string | null;
}

export type CalibrationState = "ok" | "due" | "expired" | "unknown";

/** Renewal window: highlight equipment from 6 weeks out. */
export const DUE_SOON_DAYS = 42;

export const EQUIPMENT: ReadonlyArray<{ key: keyof CalibrationDates; label: string }> = [
  { key: "airFlowMeters", label: "Air-flow meters" },
  { key: "pulsatorTesters", label: "Pulsator testers" },
  { key: "vacuumGauges", label: "Vacuum gauges" },
];

export interface EquipmentStatus {
  key: keyof CalibrationDates;
  label: string;
  /** ISO yyyy-mm-dd, or null when never recorded. */
  date: string | null;
  state: CalibrationState;
  /** Calendar days until expiry (negative = past), null when unknown. */
  days: number | null;
}

/** Today as a LOCAL ISO calendar date (testers are in NZ — UTC would flip the day overnight). */
export function todayIso(now: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
}

function parseIso(iso: string): number | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return null;
  return Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

/** Whole calendar days from `today` to `iso` (negative = already past). */
export function daysUntil(iso: string, today: string): number | null {
  const a = parseIso(iso);
  const b = parseIso(today);
  if (a === null || b === null) return null;
  return Math.round((a - b) / 86_400_000);
}

export function stateFor(iso: string | null | undefined, today: string): CalibrationState {
  if (!iso) return "unknown";
  const d = daysUntil(iso, today);
  if (d === null) return "unknown";
  if (d < 0) return "expired"; // valid through the expiry date itself
  if (d <= DUE_SOON_DAYS) return "due";
  return "ok";
}

export function equipmentStatuses(dates: CalibrationDates, today: string): EquipmentStatus[] {
  return EQUIPMENT.map(({ key, label }) => {
    const date = dates[key] ?? null;
    const state = stateFor(date, today);
    return { key, label, date, state, days: date ? daysUntil(date, today) : null };
  });
}

/** The severity that drives the page-level highlight: any expired item wins, then any due. */
export function worstState(dates: CalibrationDates, today: string): CalibrationState {
  const states = equipmentStatuses(dates, today).map((s) => s.state);
  if (states.includes("expired")) return "expired";
  if (states.includes("due")) return "due";
  if (states.includes("ok")) return "ok";
  return "unknown";
}

function spanText(days: number): string {
  if (days < 14) return `${days} day${days === 1 ? "" : "s"}`;
  const weeks = Math.floor(days / 7);
  return `${weeks} week${weeks === 1 ? "" : "s"}`;
}

/** Short human phrasing for a chip/banner, e.g. "expires in 3 weeks", "expired 5 days ago". */
export function describeStatus(s: EquipmentStatus): string {
  if (s.state === "unknown" || s.days === null) return "no date recorded";
  if (s.days < 0) return `expired ${spanText(-s.days)} ago`;
  if (s.days === 0) return "expires today";
  if (s.state === "due") return `expires in ${spanText(s.days)}`;
  return `expires ${formatDisplayDate(s.date!)}`;
}

/** dd/mm/yyyy — the same display convention as the DatePicker. */
export function formatDisplayDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
}
