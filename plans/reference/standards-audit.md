# Standards audit — wizard pass/fail vs NZMPTA Manual + ISO 6690:2007 (10 Jun 2026)

> Sources: `NZMPTA Milking Machine Testing Standards Manual.pdf` ("manual", page refs from the
> text extraction) and `ISO 6690 2007 (Mechanical Tests).pdf` ("ISO") in OneDrive
> `Clients/NZMPTA/`. 201 standards extracted; 43 findings. Re-extract text with pypdf if needed
> (the temp `_manual.txt` / `_iso6690.txt` extractions are not committed).

## Corrections applied (were wrong)

| Reading | Was | Now | Source |
|---|---|---|---|
| 4c receiver→regulator drop | ≤ 2 kPa | **≤ 1 kPa** | manual p40; ISO D.2.13 |
| 4e receiver→pump drop | ≤ 2 kPa | **≤ 3 kPa** | manual p40/p44; ISO D.2.15 |
| Effective reserve >100 clusters | clamped at 2600 | **2100 + 25/cluster above 80** (101→2625, 120→3100) | manual p42 |
| Atmos factor 102 kPa | 0.98 | **0.97** | ISO Table 4 interpolation |
| Atmos factor 103 kPa | 0.97 | **0.96** | ISO Table 4 (direct value) |
| 1a working vacuum | hard 40–50 band | **hard ≤ 50 only**; 40–50 is a guideline band (by lift height; goats/sheep run lower) | manual p40/p46 |
| 1c regulation deviation, 7c/f/i gauge errors | atMost (signed values could pass) | **± tolerance** (±2 / ±1 kPa) | manual p40; ISO D.2 |

## Standards newly enforced (were capture-only)

| Reading | Rule | Source |
|---|---|---|
| 1f min VSD-speed vacuum | rise < 2 kPa above working vacuum | manual p40 |
| 2d regulation loss | ≤ max(35, 10% of manual reserve) | manual p40; ISO C.4.6 |
| 2f regulator leakage | ≤ max(35, 5% of manual reserve) | manual p39/41; ISO C.4.8 |
| 5b regulator sensitivity | ≤ 1 kPa | manual p40; ISO D.2.6 |
| 10b vacuum system leakage | ≤ 5% of pump capacity (from 9b/8a) | manual p41; ISO C.5.4 |
| 10d milk system leakage | ≤ 10 + 2/cluster | manual p41; ISO C.5.6 |
| 11b ACR consumption (+ milk meters) | ≤ roundup10(max(30, 7.5/unit)) ×2 with bail gates | manual p41 |
| teat spray / vacuum-operated gates | ≤ 10/cluster | manual p41 |
| 14d pulsator consumption | ≤ 30 per 10 units (requiredAirflow now actually used) | manual p41 |
| 15a max chamber vacuum | ≥ working vacuum − 2 | manual p40; ISO D.2.17 |
| 13a/b/c per-cluster rows | total ≤ 12 (vented ≤ 35) / leakage ≤ 2 / air vent ≥ 4 | ISO Table D.6; manual pp41–42 |
| Pulsator rows: phase b / phase d / limp | b ≥ 30%, d ≥ 150 ms, limp ≤ 5% (per-cell + summary) | ISO Table D.5; manual p68 |
| Ratio spread | compared per quarter group (front/front, back/back) — front-vs-back differences are by design | manual pp51–53 |

## Atmospheric compensation (the c-factor) — now applied

- **Rule (manual p31, ISO §5.3.2/C.4.1/C.5.1):** the **measured** airflow (effective reserve,
  pump capacity) is **multiplied** by the factor, then compared to the unchanged standard.
  Equivalently we set the raw threshold to `required ÷ factor` so the live indicator works on the
  raw entry; the hint shows both. ISO trigger: ambient differs >3 kPa from the altitude's standard
  pressure (Table 3: <300 m → 100 kPa … 1700–2200 m → 80 kPa); legacy/manual practice is simply
  the p31 table keyed by prevailing pressure, which we follow.
- Effective reserve (2a): threshold = max(table ER, cleaning reserve) ÷ factor.
- Pump capacity (8a/9b): capture-only (OEM-curve comparison) — hint reminds to apply ×factor.
- The relative 5%/10% leakage rules are factor-invariant (both sides scale), left on raw values.
- Field renamed "Atmospheric pressure (kPa)" — the table keys on prevailing pressure, not sea level.

## Cleaning reserve (manual p43) — implemented

CR = π/4 × d² × 8 × ((100 − v)/100) × 0.06, d = milkline **internal** diameter (OD − 2 mm wall),
v = working vacuum rounded **up**. Reproduces both manual worked examples exactly (OD75 @44 →
1125; OD50 @46 → 469). Governs (max of ER/CR) when `flushingPulsationSystem` is set.

## Confirmed correct (no change)

3f/3g/3h regulation 2 kPa; 2a ER table values ≤80 clusters (odd counts round up — conservative);
30-per-10-units pulsator allowance; 12b cluster air admission 4–12 (vented ≤35); peak regulator
load ≤2 kPa; pulsator airline stability ≤4 kPa; rate spread ≤6 ppm; ratio variation ≤5%;
ATMOS rows 90–100 kPa.

## Deferred (need data/flowcharts or NZMPTA confirmation)

- **Per-model pulsator rate/ratio bands** (manual pp49–53; ISO: nominal ±5%/±5 points) — join the
  rows to the Pulsator catalog (PULRateMin/Max etc. already in the legacy table).
- **Pump OEM capacity/speed tables** (manual pp8–30, 60) + exhaust limits (Masport vane ≤13 kPa)
  — needs a pump reference table (image-only pages in the extraction).
- **Releaser model speed/power table + diaphragm dead-end test ≥ 85 kPa** (manual pp32–36, 61).
- **Vented-liner relief**: if reserve fails with vented liners, subtract 8 L/min/cluster from
  measured CAA and re-evaluate (manual p43) — cross-test logic.
- Safety-valve activation check alongside peak regulator load (manual p61).
- 6a/6b (reserve off cluster) and 15b acceptance limits — not found in the extracted text
  (manual pp58–59 are image-only); confirm before adding rules.
- Row-table failures (pulsator/cluster) don't flow into the Fault Summary yet.
