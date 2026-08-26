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

## Legacy reference-data audit (27 Aug 2026) — `Autorep_bak`

Every reference/standards table in the legacy database was profiled and compared against what the
wizard implements. **Correction to the previous version of this file: the pump, releaser and
pulsator figures were recorded as blocked on NZMPTA because the manual pages are image-only scans.
They are not blocked — the numbers are already in `Autorep_bak`.** They were only ever missing from
the *extraction*, not from the data we hold.

Note the legacy DB is **not** automatically the authority: several rows below were deliberately
changed away from it (see "Corrections applied"), where the manual or ISO says otherwise. Treat it
as the source for *catalogue numbers*, and the manual/ISO as the source for *rules*.

Extracted by `tools/reference-data/extract_legacy_reference.py` (re-runnable, deterministic) into
`Client/reference/`, typed by `Client/reference/standardsData.ts`.

### Legacy reference tables — status after wiring (27 Aug 2026)

| Legacy table | Rows | Holds | Status |
|---|---|---|---|
| `Pulsator` | 127 | rate/ratio bands, phase b/d, max chamber vacuum | **WIRED** — `pulsatorSummary(rows, model)` judges slowest/fastest rate and lowest/highest ratio against the model band (the legacy `PulsationSystemResultRange` tick/cross); verdicts shown in the Pulsator step + Test Summary PDF |
| `MinSpeedPowerCal` | 75 | clusters × heads → min speed, min power | **WIRED** — `add.releaserHeads/Speed/Power` readings; ≥ rules from the table (the legacy `SpeedO`/`PowerO` ticks). Table covers 6–40 clusters; outside it stays capture-only, as legacy was |
| `VPModel` | 140 (16 makes) | `MinRPM`, `MaxRPM`, `AirFlow`, `MotorSizeFactor` | extracted + typed (`standardsData.ts`), **no rule** — machine config doesn't capture pump make/model yet; rule lands with the Machine-Config full-fidelity rebuild |
| `MilkPumps` | 45 | `MPMin`, `MPMax`, `MPSize`, `MPMotor` | extracted + typed, **no rule** — same missing config fields |
| `ReserveReceiver` | 12 | milkline Ø × working-vacuum band → required receiver reserve | extracted + typed, **no rule** — looks like the 6b standard but 6a/6b semantics are the open NZMPTA question; do not wire on a guess |
| `LinerShellMatching` | 4,103 | shell × liner → match code 0–3 | **NOT extracted — deliberate.** Josh's call 27 Aug 2026: compatibility stays a manual tester judgement (`liner.shellCompatibility` checklist item) |
| `LinerCupNippleMatching` | 1,955 | liner × cup-nipple → `JetterMatch` | **NOT extracted — deliberate**, same call |

Legacy `MaxChamberVacuum` per pulsator model is carried in the band data but **not** wired into 15a:
the wizard's 15a rule is "within 2 kPa of working vacuum" (ISO D.2.17) and the per-model field's
semantics (sample value 10) don't obviously reconcile — resolve before using it.

### Verified as matching

- `EffectiveArea.EffectiveReserve` rows 1–80 match `EFFECTIVE_RESERVE` in `passfail/standards.ts`
  exactly (260, 260, 320, 320 … 2100, 2100).
- `EffectiveArea.Airflow_Consumption` matches `requiredAirflow()` (30 L/min per 10 units).
- `AtmosPressure` matches `ATMOS_PRESSURES` on 14 of 16 rows; the two that differ (102, 103 kPa)
  are the **deliberate ISO Table 4 corrections** already recorded above — do not "fix" them back.

### Open questions — do not guess

1. **Two conflicting effective-reserve tables.** `EffectiveArea` steps in pairs (2→260, 4→320,
   10→500, 20→600); `EffectiveReserve` is finer-grained and consistently higher (2→290, 4→355,
   10→510, 20→610). The app implements the `EffectiveArea` values. If `EffectiveReserve` is the
   operative table, the app **under-requires** reserve on every even cluster count. Resolve against
   manual p42 before shipping either.
2. **Effective reserve, 81–100 clusters.** The app extrapolates `2100 + (clusters − 80) × 25`,
   which gives 2125 at 81 clusters; `EffectiveArea` has real rows there and continues the paired
   pattern — 81→2150, 82→2150, 83→2200 (i.e. `2100 + ceil((c − 80) / 2) × 50`). The formula was
   introduced to fix the >100 clamp and appears not to have been checked against rows 81–100. Odd
   counts above 80 are currently under-required. Confirm against manual p42.
3. ~~`LinerShellMatch` code semantics~~ **Resolved by decision, 27 Aug 2026:** compatibility stays
   a manual tester judgement — the matrices are not extracted and no rule will consume them.
4. **Catalogue drift.** Legacy `SHELLS` 63 vs `shells.json` 61; legacy `Liners` 153 vs
   `liners.json` 147. Pulsators match at 127. Confirm whether the missing entries were dropped
   deliberately (deprecated) or lost.
5. **`isActive = 0` on every row** of `Pulsator`, `VPModel` and `MilkPumps`. A live system with no
   active pulsator models would be unusable, so the flag looks vestigial — confirm before filtering
   on it during migration.

## Still deferred (genuinely need flowcharts or NZMPTA confirmation)

- **Second "Additional Tests" flowchart** — the order and branching of the remaining
  `MMAdditionalTR` fields. The wizard covers ~8 of ~40 fields; this is the real blocker there.
- **Diaphragm dead-end test ≥ 85 kPa** (manual pp32–36, 61) — the rule, not the model table.
- **Vented-liner relief**: if reserve fails with vented liners, subtract 8 L/min/cluster from
  measured CAA and re-evaluate (manual p43) — cross-test logic.
- Safety-valve activation check alongside peak regulator load (manual p61).
- 6a/6b (reserve off cluster) and 15b acceptance limits — not found in the extracted text
  (manual pp58–59 are image-only); confirm before adding rules.
- Row-table failures (pulsator/cluster) don't flow into the Fault Summary yet.
