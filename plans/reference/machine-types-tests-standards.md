# AutoRep — Machine types, catalogs, tests & standards

> Snapshot 10 Jun 2026. This document is a human-readable view of data that lives in the app —
> the **source of truth is the files/tables cited in §1**, so regenerate/update this doc when
> they change. Standards were verified against the NZMPTA Testing Standards Manual + ISO
> 6690:2007 on 10 Jun 2026 — full audit trail in [standards-audit.md](standards-audit.md).

## 1. Where each kind of data lives

| Data | Lives today | Source it was seeded from | Future home |
|---|---|---|---|
| Machine configuration (per test) | `MachineConfigurations` table (server) + IndexedDB | tester entry | as-is |
| Brand/model catalogs (pulsators, shells, liners) | `Client/reference/*.json` (bundled) | legacy `Lookup` + `Pulsator` tables | admin-managed reference tables, synced to IndexedDB |
| Option lists (milkline sizes, configs, atmos pressures) | `Client/reference/lookups.ts` | legacy `Lookup` / `AtmosPressure` | same as above |
| Standard fault wording per visual check | `Client/reference/faultObservations.json` | legacy `Lookup` (92 categories) | admin-managed |
| Fault severity + recommendations | `Client/reference/faultRatings.ts` | CMM Ratings catalog (113 rows) | admin-managed |
| Pass/fail standards (thresholds & formulas) | `Client/passfail/standards.ts` + `pulsatorStats.ts` (code, pinned by 44 tests) | NZMPTA manual + ISO 6690 | admin-managed reference data |
| Full test capture | IndexedDB → `MachineTests.PayloadJson` (server) | tester entry | as-is |

So: the **catalogs** are data (bundled JSON seeded from the legacy DB — not yet admin-editable
server tables), and the **standards** are code rules with document citations. Neither is "just a
table in the database" yet; that's the planned reference-data milestone.

## 2. Machine configuration — what the tester declares

- **Plant types:** Herringbone lowline (LLHB) · Herringbone highline (HLHB) · Rotary · Other
- **Vacuum pump lubrication:** Oil-lubricated · Liquid ring · Other
- **Counts:** clusters (plant size — drives most standards) · herd size · pulsators · vacuum pumps
- **Equipment flags (drive which tests appear):** VSD · ISO test ports · pulsator-stop system ·
  ACRs · bail gates · milk meters · teat sprayer · backing gate · releaser pump · vented liners ·
  flushing pulsation system (→ cleaning reserve governs)
- **Catalog-backed dropdowns:** pulsator brand → model · shell · front liner · back liner ·
  milkline size · pulsator configuration · atmospheric pressure
- **Other:** plant size text, last BMCC, calibration expiry dates (air-flow meters, pulsator
  testers, vacuum gauges — on the Setup step)

## 3. Catalogs (seeded from the legacy database)

| Catalog | Entries | Notes |
|---|---|---|
| Pulsator models | **127** across **13 brands** — Dairymaster 1, DeLaval 26, Flynn 2, GEA 25, Milfos 6, Milkaware 3, MilkTechNZ 1, NDA 1, Nu Pulse 9, Read 1, SAC 32, Waikato 17, Wallace 3 | legacy `Pulsator` table also holds per-model rate/ratio bands + max clusters/pulsator — **not yet enforced** (task #10) |
| Shells | **61** | `shells.json` |
| Liners | **147** | `liners.json` |
| Milkline sizes | 50 · 63 · 75 · 100 mm | feeds the cleaning-reserve formula |
| Pulsator configurations | 2×2 · 4+0 | |
| Atmospheric pressures | 90–105 kPa with correction factors 1.16 → 0.94 | 102/103 corrected to ISO Table 4 (0.97/0.96) |
| Visual-check fault lists | **92 checks**, **186 standard fault observations** | OK/Fault + dropdown per check |
| CMM fault ratings | 113 faults / 13 components → severity + recommendation | auto-fills severity + recommendation |

## 4. The test workflow (wizard steps ↔ ISO groups)

| Step | Content | ISO groups |
|---|---|---|
| 1. Farm & Your Details | farm snapshot + calibration expiry dates | — |
| 2. Machine Configuration & Ancillary | §2 above (4 tabs) | — |
| 3. Visual Faults — Pre-Start | 18 checks + belt sizes (vacuum pumps, releaser groups) | Part One |
| 4. Visual Faults — Running | 18–20 groups, ~70 checks + data fields | Part Two |
| 5. Test Record | ~45 readings: system vacuum 1a–1e, VSD 1f, reserve 2a–2f, regulation 3a–3h, airline drop 4a–4e, sensitivity 5a–5b, reserve-off-cluster 6a–6b, gauge accuracy 7a–7i, pump test 8a–8c (per pump), exhaust 9a–9b | 1–9 |
| 6. Additional Tests | leakage 10a–10d, ACR 11a–11b, cluster air 12a–12b, milk meters, teat spray, gates, releaser, peak regulator load | 10–12 + |
| 7. Pulsator Test Results | per-pulsator rows (rate, ratios, phase b/d, chamber vac, limp) + 14a–14f, 15a–15b, stability | 14–15 |
| 8. Individual Cluster Tests (optional) | per-cluster rows (total air, leakage, air vent) | 13 |
| 9. Fault Summary & Recommendations | all faults grouped, severity counts, per-fault recommendations | — |
| 10. Review & Sign-Off | summary + attestation + mark complete → sync | — |

Branching: rotary vs herringbone sections · VSD adds 1f · ACR/milk-meter/teat-spray/gates/releaser
sections appear only when fitted · no ISO ports → short test flag.

## 5. Standards — every enforced pass/fail rule

All verified 10 Jun 2026 (manual + ISO page refs in [standards-audit.md](standards-audit.md)).

### Vacuum system (Test Record)
| Reading | Standard |
|---|---|
| 1a working vacuum @ receiver | ≤ 50 kPa hard · 40–50 guideline by lift height |
| 1c regulation deviation | ± 2 kPa |
| 1f vacuum @ min VSD speed | rise < 2 kPa above working vacuum |
| 2a effective reserve | ≥ table value (260 @2 clusters … 2100 @80; +25/cluster above 80); **measured × atmospheric factor**; cleaning reserve governs when flushing system fitted |
| 2d regulation loss | ≤ 10% of manual reserve or 35 L/min (greater) |
| 2f regulator leakage | ≤ 5% of manual reserve or 35 L/min (greater) |
| 3f / 3g / 3h fall-off, undershoot, overshoot | each ≤ 2 kPa |
| 4c drop receiver → regulator | ≤ 1 kPa |
| 4e drop receiver → pump | ≤ 3 kPa |
| 5b regulator sensitivity | ≤ 1 kPa |
| 7c/7f/7i farm-gauge error (3 points) | ± 1 kPa |
| 8a/9b pump capacity | capture + × atmospheric factor → OEM curve (table pending) |
| 9a exhaust pressure | per manufacturer (Masport vane ≤ 13 kPa) |

### Airflow & ancillaries (Additional Tests)
| Reading | Standard |
|---|---|
| 10b vacuum system leakage | ≤ 5% of pump capacity |
| 10d milk system leakage | ≤ 10 + 2 per cluster L/min |
| 11b ACR consumption / milk meters | ≤ round-up-10(max(30, 7.5 × units)); ×2 with bail-gate rams |
| 12b cluster air admission | 4–12 L/min per cluster (vented liners ≤ 35) |
| teat spray / vacuum-operated gates | ≤ 10 L/min per cluster |
| peak regulator load | increase ≤ 2 kPa |

### Pulsation
| Check | Standard |
|---|---|
| rate spread (fastest − slowest pulsator) | ≤ 6 ppm |
| ratio variation between pulsators | ≤ 5% (front vs front, back vs back — front/back may differ by design) |
| limping (within cluster) | ≤ 5% |
| phase b | ≥ 30% |
| phase d | ≥ 150 ms |
| 14d pulsator consumption | ≤ 30 L/min per 10 units |
| 15a max chamber vacuum | within 2 kPa of working vacuum |
| pulsator airline stability | dips ≤ 4 kPa |
| per-model rate/ratio band | **pending** (catalog has the data — task #10) |

### Individual clusters (ISO 13 / Table D.6)
| Check | Standard |
|---|---|
| total air admission | ≤ 12 L/min (vented ≤ 35) |
| cluster leakage (vent closed) | ≤ 2 L/min |
| air-vent admission | ≥ 4 L/min |

### Formulas
- **Cleaning reserve** (flushing/slug systems): CR = π/4 × d² × 8 × ((100 − v)/100) × 0.06,
  d = milkline internal Ø (OD − 2 mm), v = working vacuum rounded up. Requirement = max(ER, CR).
- **Atmospheric correction**: corrected airflow = measured × factor (90 kPa → 1.16 … 100 → 1.00
  … 105 → 0.94); applied to effective reserve + pump capacity.

## 6. Known gaps (deferred — task #10)

Per-model pulsator bands · OEM pump capacity/speed/exhaust tables (manual pages are image-only
scans) · releaser model speed/power + diaphragm dead-end ≥ 85 kPa · vented-liner CAA relief
(−8 L/min/cluster on reserve fail) · safety-valve activation check · 6a/6b + 15b acceptance
limits (confirm with NZMPTA) · row-table failures → Fault Summary.
