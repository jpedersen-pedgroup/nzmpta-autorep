// Display names for the Machine Configuration enums. The stored values are the .NET enum names
// (types.ts) — "HerringboneLowline", "OilLubricated" — which is what the report was printing.
import type { PlantType, PumpLubrication } from "./types";

export const PLANT_LABELS: Record<PlantType, string> = {
  HerringboneLowline: "Herringbone (lowline)",
  HerringboneHighline: "Herringbone (highline)",
  Rotary: "Rotary",
  Other: "Other",
};

export const PUMP_LUBRICATION_LABELS: Record<PumpLubrication, string> = {
  OilLubricated: "Oil lubricated",
  LiquidRing: "Liquid ring",
  Other: "Other",
};
