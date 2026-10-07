import { describe, it, expect } from "vitest";
import { exactFarm, farmDetail, matchFarms, wizardUrlFor } from "./NewTestApp";
import type { CachedFarm } from "../sync/farmsSync";

const farms: CachedFarm[] = [
  { id: "1", name: "Kowhai Flats", supplyNumber: "40123", town: "Te Awamutu", milkCompanyName: "Fonterra" },
  { id: "2", name: "Rimu Ridge", supplyNumber: "50999", town: "Morrinsville" },
  { id: "3", name: "Smith Farm", town: "Gore" },
  { id: "4", name: "Smith Farm", town: "Matamata" },
];

describe("the New-test farm picker", () => {
  it("searches name, supply number and town", () => {
    expect(matchFarms(farms, "kowhai").map((f) => f.id)).toEqual(["1"]);
    expect(matchFarms(farms, "50999").map((f) => f.id)).toEqual(["2"]);
    expect(matchFarms(farms, "matamata").map((f) => f.id)).toEqual(["4"]);
    expect(matchFarms(farms, "  ")).toHaveLength(4);
  });

  it("caps the list", () => {
    const many = Array.from({ length: 80 }, (_, i) => ({ id: String(i), name: `Farm ${i}` }));
    expect(matchFarms(many, "")).toHaveLength(50);
  });

  it("starts on a typed name that matches one farm exactly", () => {
    expect(exactFarm(farms, " rimu ridge ")?.id).toBe("2");
  });

  it("won't guess between two farms with the same name", () => {
    expect(exactFarm(farms, "Smith Farm")).toBeNull();
    expect(exactFarm(farms, "")).toBeNull();
  });

  it("tells same-named farms apart in the list", () => {
    expect(farmDetail(farms[0])).toBe("Supply 40123 · Te Awamutu · Fonterra");
    expect(farmDetail(farms[2])).toBe("Gore");
  });

  it("opens the wizard with the farm — no server round trip", () => {
    expect(wizardUrlFor({ id: "f-1", name: "Kowhai & Sons" })).toBe("/App/Tests/Wizard?farmId=f-1&farmName=Kowhai+%26+Sons");
  });
});
