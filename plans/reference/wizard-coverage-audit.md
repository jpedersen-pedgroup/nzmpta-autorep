# Wizard coverage audit — legacy capture vs current wizard (9 Jun 2026)

The offline wizard was built with **condensed/representative** field sets per step. This audit
compares it to the legacy `Autorep_bak` capture tables (the source of truth) and quantifies the
gap. **Plan: rebuild every step to full legacy fidelity.**

| Step | Legacy table(s) | Legacy fields (approx) | Wizard now | Gap |
|---|---|---|---|---|
| Setup / Machine config | `TestDairyFarmInfo`, `TestVaccumPumpDetails`, `Tests` | ~55 | ~20 | counts (bail gates/milk meters/pulsators), brands, last BMCC, equipment calibration dates, atmospheric pressure, no. of milk/vacuum systems, foam disperser, drive-milk-pump, regulator/releaser make/model… |
| Visual Faults — Pre-Start | `VisualFaultsMMStart` | 18 checks + 2 belt sizes | 17 checks | **Oil/Water Supply Protected**, the **belt-size data fields**, legacy grouping (belt-driven vs diaphragm vs controls) |
| Visual Faults — Running | `VisualFaultsMMRunning1–4` | ~80 checks / ~18 groups | ~18 / 4 groups | **Claw, Liner, Shell, Pulse/Milk tubes (SPT/LPT/LMT), Platform, Milk-Flow-Indicator, ACR, Milk meter, Pulsation, Vacuum gauge, Regulator, Receiver, VP oil/water (running), Jetters** + data fields (diameters/lengths/run-time) |
| Test Record | `MMTestRecords1–3` | ~100 readings (ISO 1–15) | 4 | most of ISO 1–15: vacuum levels 1a–1f, reserve 2a–2h, regulation 3a–3h, airline drop 4, regulator sensitivity 5, reserve-off-cluster 6, **gauge accuracy ×3 (7)**, **pump capacity ×4 + speeds (8)**, exhaust 9, leakage 10, ACR 11, cluster air 12, pulsator & ancillary 14, test pulsation 15, final compares |
| Additional Tests | `MMAdditionalTR` | ~40 | ~8 | releaser-pump heads ×2 pumps, bubbles/water-drawn/leakage-releaser, Other ×4, standard pump capacity, regulation leakage, ACR consumption |
| Pulsator | `PulsationSystemResult` | per-unit: rate, ratio, B/D vacuum, channel, phases A–D (%/ms), limp | 5 summary | needs **per-pulsator row table** |
| Individual Cluster | `IndividualClusterAirflow` | per-unit: total air admission, leakage, air-vent admission | 2 summary | needs **per-cluster row table** |
| Fault Summary | `MMTestSummary` / `FaultInfo` | per-category priority | aggregated faults + per-fault recs | OK — PRD intentionally upgrades to per-fault severity |

## Rebuild order
1. **Visual Faults — Pre-Start** (full) — in progress
2. **Visual Faults — Running** (full; expand the resolver's running sections + config-gating, update fixtures)
3. **Test Record** (full ISO 1–15; many readings + their standards)
4. **Additional Tests** (full)
5. **Pulsator + Individual Cluster** (per-unit row tables)
6. **Machine Config** (remaining declared fields)

Notes: the data-capture fields (belt sizes, diameters, lengths, pulsator run-time) are not OK/Fault
checks — they need a **data input** (added to the checklist model). Per-unit steps (pulsator,
cluster) need a **row-table** UI distinct from the single-reading pattern.
