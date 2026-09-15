# Tester feedback, 15 Sep 2026 — fix plan

> **Source:** Teams recording *Auto Rep — Recorded 20260915 080220* (48 min), Josh Pedersen with Jono Rowlands. Jono ran a real Machine Test in the installed PWA on Thu 11 Sep alongside the legacy app, then walked through where it tripped him up. `⏱ 12:15` = time into the recording.
> **Assessed against:** `main` @ `7b7e94c` (27 Aug 2026), the build Jono used. Code claims cite a file:line that was read. Legacy-app labels and formulas are transcribed from the screen share, not from code.
> **Paths** are relative to `src/Autorep.Web/` unless they start with `tests/`, `tools/` or `plans/`.
> **Legend:** ✅ agreed in the meeting · ❓ needs a decision (owner named) · 🔎 found while preparing this plan, not raised in the meeting

---

## The short version

- **The app never calculates a reading.** Every value the legacy app derives (1c, 2d, 2f, 3f–3h, 4c, 4e, 5b, 7c/f/i, 10b, 10d, 11b, 12b, 14b/d/f, 15b) is an empty, editable box the tester has to work out and type. Only pass/fail *limits* are derived. A reading definition has no formula (`Client/passfail/standards.ts:63-69`), and the only thing that writes a reading is the input handler (`Client/wizard/ReadingsStep.tsx:37-41` → `Client/wizard/WizardApp.tsx:302-307`). That is the "which ones do I fill in?" confusion, and it has already produced a **wrong PASS**: Jono's report prints `1c −1 kPa PASS` from a typed value, but 1a 41 − 1b 45 = −4 (legacy, on the true 41.5: −3.5 ✗).
- **12b is judged per cluster but measured as a total.** Legacy: 12b = 10c − 12a = 4320 − 3850 = 470 L/min, judged against 4–12 L/min × clusters (37 clusters → 148–444 → FAIL). The app labels 12b "per cluster" and applies 4–12 to it, so any real shed fails.
- **Decimals break while typing.** Readings are controlled as numbers, so `41.` is rewritten mid-keystroke and the caret jumps. On screen, "41.5" turned into "541 FAIL" (⏱ 12:16).
- The rest is mostly small: labels, duplicated fields, one CSS rule hiding half a step, missing dropdowns, and a general-comments box whose storage and sync already exist.

## Plan at a glance

| Phase | Covers | Size | Waiting on |
|---|---|---|---|
| **0** Quick wins | F3, F8, F9, F11 (rename), F12, F14, F20, report cosmetics | S | — |
| **1** Calculated readings | F7, F15 | M | 14d limit, vented-liner 12b (❓). Ship the rest without them |
| **2** Flow and show-on-fail | F10, F11 (order), F16, F17 | M | step structure (❓ Josh); real 12b from Phase 1 |
| **3** Capture gaps | F1, F2, F4, F5, F6 | M–L | airline bends (❓ Jono), shell grouping (❓ Maria) |
| **4** Wording and report | F13, F19 | M | wording (❓ Maria), audit log (❓ Josh) |
| **5** Connectivity check | F21 | S | — |

Ship Phase 0 on its own first and have Jono re-run a test on staging. Then Phase 1, Phase 2 once 12b is real, and Phases 3–4 as answers come back. Phase 5 can run in parallel.

**Progress, 15 Sep 2026 (evening).** Phase 0 → PR #54, merged. Phase 1 → PR #55, merged (plus a PWA "updated — reload" banner and two worker-cache fixes found on the way). Both on staging. Phase 2 (2.1 order — Josh's decision: vacuum → airflow → individual cluster → pulsation → additional; 2.2 cluster step only when 12b fails; 2.3 pulsation spread judged on the analyser's fastest/slowest and highest/lowest, the faulty-pulsator table only on a failed reading; 2.4 sections 8 and 9 merged) and Phase 3 items 3.1–3.3 (choice items; cluster position Side/Back; pulse-tube type Single/Twin/Triple/Quad; recorded measurements printed on the report) → branch `claude/tester-feedback-phase2`. An adversarial review of that branch found nothing above "low" except one: a transposed fastest/slowest (or highest/lowest) entry gave a negative spread that passed — the spreads are now order-independent. Also from it: the stored step is normalised to a shown one on every save, the faulty-pulsator table stays open while any row exists, the spread hints read the resolved rule, the choice items use the themed Select, and the row-based spread stats (dead since 2.3) are gone. Still open: 3.4 (Jono), 3.5 (Maria), 3.6 pump/regulator/releaser catalogues, Phases 4–5, the migrated-data formula check, and the pooled-vs-per-group ratio spread question below.

## Feedback register

| # | Feedback | ⏱ | Status | Phase |
|---|---|---|---|---|
| F1 | Bail area "Cluster position (mm)" should be Side / Back | 2:34 | ✅ | 3 |
| F2 | Main airline "Airline bends size": Jono doesn't recognise it; legacy measured an effective length? | 4:05 | ❓ Jono | 3 |
| F3 | Claw / shell / liner *type* re-asked in Visual Faults, already in Machine Configuration | 5:15 | ✅ | 0 |
| F4 | Should shell condition sit under Claw? | 5:47 | ❓ Maria | 3 |
| F5 | Pulse tubes (long and short) need a tube type: single / twin / quad | 7:00 | ✅ | 3 |
| F6 | No vacuum pump make / model / size, regulator, or milk (releaser) pump | 8:27 | ✅ | 3 |
| F7 | Calculated values don't populate; can't tell inputs from calculated | 10:11 | ✅ | 1 |
| F8 | Can't enter decimals (41.5) | 12:15 | ✅ | 0 |
| F9 | 8c "Speed @ 50 kPa" → "Max speed", to pair with min speed | 13:45 | ✅ | 0 |
| F10 | 9b belongs with the vacuum pumps, spec beside the tested value: "combine 8 and 9" | 14:30 | ✅ | 2 (+3) |
| F11 | "Pulsator Test Results" is really pulsation and ancillary; tests are out of flowchart order | 15:41 | ✅ | 0 / 2 |
| F12 | Pulsators: record failing units only, by unit (bail) number, e.g. 27 > 19; drop "Enter all (19)" | 21:20 | ✅ | 0 |
| F13 | Reservoir wording ("20 mm above the shaft centreline") is for water pumps only; split oil vs water | 24:14 | ✅ / ❓ Maria | 4 |
| F14 | Pulsator step: readings first, faulty-pulsator table below; Step rail hid the readings | 27:37 | ✅ | 0 |
| F15 | 12b maths must scale with the cluster count | 33:25 | ✅ | 1 |
| F16 | Individual Cluster Tests only when 12b fails | 35:59 | ✅ | 2 |
| F17 | Faulty-pulsator table only when the readings fail | 36:37 | ✅ | 2 |
| F18 | Pulsation PDF upload | 37:44 | Works. The error dialogs came from DeLaval PC200, not AutoRep | — |
| F19 | Report: page 1 is what the farmer needs, numbers later; where the audit log goes | 40:06 | ✅ / ❓ Josh | 4 |
| F20 | General comments box on Fault Summary (step 9) | 42:09 | ✅ | 0 |
| F21 | Connection trouble in the installed app (hotspotted) | 2:08 | check | 5 |

---

## Phase 0 — quick wins

No data-model or rule changes; one PR.

**0.1 Decimals (F8).** `ReadingsStep.tsx:32-42` is `type="number" step="any"`. The problem is that `value` is bound to the stored number and input is parsed with `Number(raw)`, so every keystroke round-trips through a number. `41.` doesn't parse: Chromium reports an empty value, the reading is dropped (`WizardApp.tsx:304`) and the field re-renders, which moves the caret. What the screen shows matches this. Row-table cells (`Client/ui/RowTable.tsx:102-111`) are `type="number"` with no `step`, so decimals are flagged invalid there too.
- Add `Client/ui/DecimalInput.tsx`. It keeps the raw text while focused, commits only when it parses, uses `inputMode="decimal"` (tablet keypads), accepts `,` and allows a leading `-`. Use it in `ReadingsStep` and `RowTable`.
- Keep Machine Configuration counts as whole numbers, because the server fields are `int` (`Api/SyncController.cs:32-36`).
- Readings already travel as JSON into `nvarchar(max)`, so no server change is needed.

**0.2 8c label (F9).** Change `Client/passfail/standards.ts:287` from "Speed @ 50 kPa (8c)" to "Maximum speed (8c)". The key `tr.pumpMaxSpeed{i}` already means maximum (legacy `VPCPumpSpeedMaximum{i}E`, `Client/report/legacyAdapter.ts:113`), and legacy labels it "Pump Speed Minimum / Maximum" (⏱ 16:24).

**0.3 Duplicate type fields (F3).** Delete `claw.type` and `claw.shellType` (`Client/wizard/visualChecklist.ts:141-142`) and `liner.typeF` and `liner.typeB` (`:154-155`). Machine Configuration already captures claw, shell and front/back liner (`Client/wizard/MachineConfigStep.tsx:221-241`).
- Keep their labels in the amendment-history label map so older tests don't show raw keys (`Client/versioning/amendments.ts:79-85`).
- Optional: show the configured values read-only in those tabs.

**0.4 Pulsator rows (F12).**
- Remove "Enter all" from `RowTable` altogether (`RowTable.tsx:31-36`): the pulsator table (`Client/wizard/PulsatorStep.tsx:84`) and, per Josh, the Individual Cluster Tests table too — testers only record the units that failed.
- New rows start with an empty, focused unit field instead of `rows+1` (`RowTable.tsx:28-29`, which also repeats numbers after a delete).
- Rename the column from `unitLabel="Pulsator"` (`PulsatorStep.tsx:83`) to "Unit no." (legacy wording, ⏱ 23:58), and update the hint at `:75-77`.
- The field is already free text (`RowTable.tsx:86-94`), and 27 was accepted on screen (⏱ 23:25). Warn, don't block, on numbers above the cluster count and on duplicates.
- Jono said typing or a dropdown would both do (⏱ 23:34). Typing is simpler.

**0.5 Pulsator step layout (F14).**
- Swap the order in `PulsatorStep.tsx:71-141` so the ISO 14/15/Stability `ReadingsStep` comes first and the faulty-pulsator card sits below it.
- Fix the Step-rail bug. `wwwroot/css/site.css:847` gives every `.wizard__panel > .card` `min-height: 100%` of the 650 px lane (`:844-846`). The pulsator step renders two sibling cards, so the first card, empty, filled the lane and pushed "Pulsator & ancillary readings" off-screen. Jono thought it was missing until Josh said to scroll (⏱ 29:19).
- To fix it, scope the rule to `.card:only-child`, or wrap multi-card steps in one container, and check the other steps that render more than one card.

**0.6 Rename the pulsator step (F11, part).** Rename "Pulsator Test Results" to "Pulsation & Ancillary (ISO 14–15)". Jono: "it's actually pulsation and ancillary equipment"; Josh: "this is not the pulsator" (⏱ 16:47–17:41).
- The title is defined twice and the two must match: `Client/wizard/wizardStepResolver.ts:84` and `Domain/Wizard/WizardStepResolver.cs:25`. Update `tests/fixtures/wizard/*.json` if they assert titles.
- Inner cards: "Pulsator & ancillary readings", then "Faulty pulsators".

**0.7 General comments (F20).** The storage already exists. `notes` goes device → sync → server (`Client/db/testStore.ts:110` → `Client/sync/syncClient.ts:75` → `Domain/Entities/MachineTest.cs:24`; `Api/SyncController.cs:41,122,149`), and amendment history labels it "Tester comment" (`amendments.ts:243`). It is only *shown* for migrated legacy tests (`Client/wizard/FaultSummaryStep.tsx:68-112`, `Client/report/testSummaryPdf.ts:370-390`).
- Add an editable "General comments" textarea to step 9 (`FaultSummaryStep.tsx`), bound to `notes`.
- Print it straight after the fault summary (`testSummaryPdf.ts:123-142`) on every report.
- Jono's examples (⏱ 42:46–44:10): dirty clusters and shells as a milk-quality risk; regulation undershoot, settings adjusted on the day; nominal vacuum above working vacuum but fine for this herd. Today he has to attach these to faults that don't match.

**0.8 Report cosmetics (🔎, seen on Jono's report).**
- Raw enum values: `testSummaryPdf.ts:111` prints `config.plantType` ("HerringboneLowline") and `:115` prints `config.pumpLubrication` ("OilLubricated"). Use the Machine Configuration display labels.
- UTC date in the filename: `testSummaryPdf.ts:436-438` slices `markedCompleteAt`, so a test completed at 8:41 am on 15 Sep saves as `… 2026-09-14.pdf`. Format the date in `Pacific/Auckland`; this is the same kind of bug as #53.

---

## Phase 1 — calculated readings (F7, F15)

**Design.**
- Give the reading definition (`standards.ts:63-69`) an optional formula made of inputs plus a calculation. One pure function, `deriveReadings(config, readings)`, applies them all. Some formulas cross steps: 10b needs 9b, and 14b needs 12a.
- Run it in `setReading` (`WizardApp.tsx:302-307`) and **store** the results. Then these all work unchanged: progress (`Client/wizard/wizardProgress.ts:58-63`), faults (`Client/faults/buildFaults.ts:35-48`), the report (`testSummaryPdf.ts:146-165`), amendments, sync and admin views.
- Storing the results also lets the Test Record step complete without the tester typing derived values, which it can't today (`wizardProgress.ts:58-60`).
- Show calculated rows read-only, with a "calculated" tag and the formula as the hint ("= 1a − 1b"), rounded to 0.1.
- Only calculate when every input is present; otherwise leave the value blank. Legacy showed −45 ✗ with 1a empty (⏱ 11:20), and that shouldn't be copied.
- Keep the existing keys. That way the legacy mapping (`Client/report/legacyAdapter.ts`), stored verdicts and admin standard overrides (`Client/passfail/standardsOverrides.ts:34-55`) keep working.
- No server change: readings are opaque JSON, and `Domain/PassFail/PassFailCalculator.cs:32-47` only runs in tests.

**Formulas.** Legacy labels come from the screen share (⏱ 11:30, 16:24, 16:30, 33:45). Rows marked † were read in one pass only; confirm them with the data check below.

| ISO | Legacy label | Calculation (keys in `standards.ts`) | Writes | Note |
|---|---|---|---|---|
| 1c | Vacuum Regulation Deviation (1a − 1b) † | `tr.workingVacuum − tr.nominalVacuum` | `tr.regulationDeviation` :111 | ±2 kPa |
| 2d | Regulation Loss (2c − 2a) | `tr.manualReserve − tr.effectiveReserve` | `tr.regulationLoss` :180 | |
| 2f | Regulator Leakage (2e − 2b) | `tr.regulatorLeakageAirflow − tr.reserveAirflow` | `tr.regulatorLeakage` :192 | |
| 3f | (3a − 3c) † | `tr.avgReceiverVacuum − tr.avgVacuumAirInlet` | `tr.fallOff` :215 | |
| 3g | (3c − 3b) † | `tr.avgVacuumAirInlet − tr.minVacuumAirInlet` | `tr.regulationUndershoot` :216 | |
| 3h | (3d − 3e) † | `tr.maxVacuumIncrease − tr.avgVacuumStopAirInlet` | `tr.regulationOvershoot` :217 | |
| 4c | (4b − 4a) † | `tr.airlineVacRegulator − tr.airlineVacReceiver` | `tr.airlineDropRR` :229 | |
| 4e | (4d − 4a) † | `tr.airlineVacPump − tr.airlineVacReceiver` | `tr.airlinePumpDrop` :232 | |
| 5b | Regulation Sensitivity (5a − 1a) | `tr.regSensWorkingVac − tr.workingVacuum` | `tr.regulatorSensitivity` :242 | |
| 7c/f/i | Gauge Error (7a − 7b) / (7d − 7e) / (7g − 7h) | `tr.farmGaugeN − tr.testGaugeN` | `tr.gaugeError1–3` :265-271 | |
| 10b | Vacuum System Leakage (Air Flow Start − 10a) | `tr.pumpCapacityTotal − add.airflowVacuumSystem` | `add.vacuumSystemLeakage` :338 | 9b *is* Air Flow Start; see 2.4 |
| 10d | Milking System Leakage (10a − 10c) | `add.airflowVacuumSystem − add.airflowMilkSystem` | `add.milkSystemLeakage` :346 | |
| 11b | ACR Consumption (10c − 11a) | `add.airflowMilkSystem − add.acrAirflow` | `add.acrConsumption` :362 | ACRs only |
| 12b | Cluster Air Admission (10c − 12a) | `add.airflowMilkSystem − add.clusterAirAdmissionConnect` | `add.clusterAirAdmission` :376 | **rule changes**, see below |
| 14b | Milk System Ancillary Equipment Leakage (12a − 14a) | `add.clusterAirAdmissionConnect − puls.airflowMilkSystem` | `puls.milkSystemAncillary` :475 | no rule today |
| 14d | Pulsator Consumption (14a − 14c) | `puls.airflowMilkSystem − puls.airflowPulsators` | `puls.pulsatorConsumption` :478 | ⚠ rule conflict, see below |
| 14f | Vacuum System Ancillary Equipment Leakage (14c − 14e) | `puls.airflowPulsators − puls.airflowVacuumSystem` | `puls.vacuumSystemAncillary` :485 | no rule today |
| 15b | Pulsator Airline Drop (1a − 15a) | `tr.workingVacuum − puls.maxChamberVacuum` | `puls.testPulsationReading` :502 | relabel; 15a's "within 2 kPa" check belongs here |

Legacy also shows 2g "Required Standard Effective Reserve" and 2h "Required Cleaning Reserve" as fields (⏱ 11:30). The app already computes both, but only as hint text on 2a (`standards.ts:153-165`). Show them as read-only calculated rows so the tester can see the target.

**12b (F15).** Judge the total against the per-cluster limits (4–12, from the admin standard; `Data/Seed.cs:239`) × `config.clusterCount`, the same way 10d uses "10 + 2 per cluster".
- Label it "Cluster air admission, total (12b)", with the hint "4–12 L/min × 37 clusters = 148–444", and show the per-cluster average beside it.
- ❓ NZMPTA, vented liners: the standards sheet says "< 35 l/min/cluster" (⏱ 35:30), and the app applies ≤ 35 per cluster when `linerVented` is set. Confirm this becomes ≤ 35 × clusters, and whether a minimum still applies.
- 🔎 Migrated tests: legacy `CAAClusterAirAdmissionE` holds the total (470 on screen), but it is mapped onto today's per-cluster key (`legacyAdapter.ts:101`). Migrated tests therefore show totals under a per-cluster label. This change fixes the meaning.

**14d ⚠.** Legacy passes 14d = 790 L/min for 19 GEA Autopuls S pulsators (⏱ 16:30). The app's rule is ≤ 30 L/min per 10 units (hint "≤ 120"), so a calculated 14d would fail every shed like this one.
- ❓ Jono / NZMPTA: what is the 14d limit? Possibly a per-model pulsator consumption.
- Until that's answered, calculate and show 14d without a verdict.

**Decisions taken in the implementation (15 Sep 2026, branch `claude/tester-feedback-phase1`).**
- **A calculated key is owned by its formula** (`Client/passfail/derived.ts`): written whenever every input is present, removed when one is missing. This replaces the "keep what was typed until the inputs exist" idea above — a flat `Record<string, number>` can't tell a typed value from a calculated one, and a stale figure is worse than a blank. Consequence for the few in-progress tests on devices: a hand-typed 1c/3f/… is replaced (or blanked) on the next edit and on open; tell the testers.
- Derivation runs on every reading **and** configuration edit (2g/2h and the 12b band depend on the config), in memory when an editable test is opened (so a test saved by an older build never shows a typed value under the "calculated" tag), and once more at sign-off. Frozen and migrated tests are never touched.
- **2g/2h are stored readings** (`tr.requiredEffectiveReserve`, `tr.requiredCleaningReserve`), so the report prints the standard the tester was judged against. Accepted cost: a reopened pre-Phase-1 test logs "Required effective reserve: — → 1050" once, and a cluster-count edit logs the 2g change beside it.
- Calculated rows never gate step completion — only typed readings do (`wizardProgress.ts` `readingKeys`). Otherwise a flushing plant with a non-numeric milkline could never complete Test Record (2h blank).
- **12b** is the machine total judged at the per-cluster admin standard × cluster count; vented liners use ≤ 35 × count and the hint says that is unconfirmed. **14d** is calculated with no verdict, via `ruleFor` so NZMPTA can enable a limit from the admin page. **15b** carries the ≤ 2 kPa drop rule (where legacy recorded it); 15a is capture-only. **10b**'s limit comes from 9b only — the 50 kPa 8a figures no longer stand in.
- Completed tests stay frozen (`WizardApp.tsx:287`); a reopened Test Version recalculates on open and records the differences in its amendment history.
- **Still unverified:** the six formulas read off the legacy screen once (1c, 3f, 3g, 3h, 4c, 4e) and 5b's sign (a negative 5a − 1a passes `≤ 1` silently — should it be `± 1`?). Both need the migrated-data check below; the legacy database isn't on the dev machine. Also assumed: legacy `TPReadingE` is the 15b drop (its verdict column is mapped to 15b, and the legacy screen shows the tick on 15b).

**Check the formulas against real data before shipping.** The migration keeps every legacy column (`tools/Migration/Pipeline/MigrationRunner.cs:43-55,575-581`). Run each formula over the migrated legacy tests (e.g. `VLVacuumRegulationE = VLVacuumReceiverE − VLNominalVacuumE`) and review the mismatches. This is also how the † rows get confirmed.

---

## Phase 2 — flow and show-on-fail (F10, F11, F16, F17)

**2.1 Follow the ISO flowchart (F11). ❓ Josh on the exact structure.**

Today the steps are:
- Step 5: ISO 1–9.
- Step 6 "Additional Tests": 10, 11 and 12, plus teat sprayer, gates, releaser, milk meters and peak regulator load.
- Step 7: pulsator table plus 14/15/Stability.
- Step 8: 13 (optional).

The flowchart Josh shared (⏱ 19:34; also `plans/reference/test-workflow-and-faults.md:9-30`) runs … 8 → 9 (only if the pump is out of spec) → Air Flow Start → 10 → 11 (if ACRs) → 12 → 13 (if 12 fails) → 14 → 15 → 16.

Jono went from 9 straight to 14 and came back for 10 and 12 (⏱ 18:18). The most likely reason is that ISO 10–12 sit under a step called "Additional Tests". For NZMPTA that name means the separate post-15 flowchart and legacy's "MM Additional Testing" page.

Recommended order:
1. Vacuum tests (ISO 1–9)
2. Airflow tests (ISO 10–12), with 13 directly after (see 2.2)
3. Pulsation and ancillary (ISO 14–15)
4. Additional tests: the unnumbered extras only

Constraints:
- Steps come from the resolver, which exists twice and must stay identical (`Client/wizard/wizardStepResolver.ts:76-91`, `Domain/Wizard/WizardStepResolver.cs:23-26`).
- It is pinned by `tests/fixtures/wizard/*.json`, `tests/Autorep.Web.Tests/WizardStepResolverTests.cs` and `Client/wizard/wizardStepResolver.test.ts`. Change both copies and the fixtures in one PR.
- Keep the report order in step (`allReadingSections`, `standards.ts:547-557`).
- Step numbers will shift, and people refer to Fault Summary as "step 9", so tell the testers.

**2.2 Individual Cluster Tests only when 12b fails (F16).** Visibility is config-only today; "Optional" only relabels the step (`wizardProgress.ts:161-175`).
- Add a client-side filter that drops the step unless 12b (`add.clusterAirAdmission`) fails or rows already exist. Never hide data that has been entered.
- Use it everywhere a step list is built: `WizardApp.tsx:423`, `overallProgress` (`wizardProgress.ts:164-175`) and `computeCompleted`.
- Keep it out of the resolver so .NET and TS stay identical. The server never calls the resolver at runtime; its only reference is the class itself (`WizardStepResolver.cs:11`).
- The report already prints the cluster table only when rows exist (`testSummaryPdf.ts:207-217`), which covers Jono's "untick it when printing" complaint (⏱ 36:24).
- This needs Phase 1: 12b has to be a real total before it can fail correctly.

**2.3 Faulty-pulsator table only when readings fail (F17).** After 0.5, show the table when any ISO 14/15 or stability reading fails, or when rows already exist. Josh: "show based on 14 answers" (⏱ 37:12).
- ❓ Jono: should there also be a "Record a faulty pulsator" link? A single bad unit doesn't always show up on both clusters (⏱ 22:43), so a failed analyser result may not move 14d.
- `pulsatorSections(config, readings)` already receives the readings (`PulsatorStep.tsx:135`).
- 🔎 The rate/ratio **spread** check is computed over the rows entered (`Client/passfail/pulsatorStats.ts:53-56`). With failed-only entry the recorded units are a subset: their spread can prove the machine fails but never that it passes. Phase 0 now shows only a FAIL (screen and report) and no PASS. Legacy captured the machine-level figures explicitly — "Rate Range Fastest / Slowest", "Ratio Range Highest / Lowest" (⏱ 23:58). Capture those four as readings here and judge the spread from them.
- 🔎 While here: pulsator row and spread failures never reach Fault Summary (`buildFaults.ts`; a known gap, `plans/reference/machine-types-tests-standards.md:135`).

**2.4 Merge 8 and 9 (F10).** "Pump capacity (9b)" (`standards.ts:298`, key `tr.pumpCapacityTotal`) is legacy's **"9A Air Flow Start: Pump Capacity at Working Vacuum"** (⏱ 16:24). It is the baseline 10b is measured against, not a second capacity test, which is why Jono wanted it "back under vacuum pumps". Make it one "8 · Vacuum pump(s)" section:
1. 8a / 8b / 8c for each pump.
2. "Air flow start: pump capacity at working vacuum", with the key unchanged.
3. 9a exhaust pressure, collapsed as the flowchart's exception ("only if pump capacity and operation are not within standards").

The section builder is `standards.ts:275-300` and the report order is `:547-557`. The OEM spec beside 8a comes with 3.6.

---

## Phase 3 — capture gaps (F1, F2, F4, F5, F6)

**3.1 A "choice" checklist item.** Checklist items can only be OK/Fault or free text (`Client/wizard/visualChecklist.ts:6-17`, `Client/wizard/VisualFaultsStep.tsx:43-55`). Add `choice: string[]`, rendered as a select and stored in `dataFields` like other data items (`WizardApp.tsx:314-319`).

**3.2 Use it (F1, F5).**
- Bail area: replace `ba.clusterPositionValue` "Cluster position (mm)" (`visualChecklist.ts:85`) with "Cluster position: Side / Back" (legacy offers N/A / SIDE / BACK, ⏱ 3:59).
  - The fault "Cluster Positioning Is Incorrect" already exists (`Data/SeedData/faultObservations.json:24`).
  - The mm value belongs to "Herringbone centres (mm)", which stays.
- Pulse tubes: add "Tube type: Single / Twin / Triple / Quad" to Short (`visualChecklist.ts:170-180`) and Long (`:181-192`). Jono named single, twin and quad; legacy also lists Triple (⏱ 7:17).

**3.3 Print visual-fault data on the report (🔎).** Nothing prints `dataFields`; only amendment history reads them (`amendments.ts:232`). Jono's reason for tube type is that whoever replaces the tubing needs to know which to buy (⏱ 7:52), and that only works if it is on the report. Print the captured values (types, sizes, lengths) with their section under Visual faults (`testSummaryPdf.ts:219-243`).

**3.4 "Airline bends size" (F2). ❓ Jono.** The field is `ma.bendsSize`, placeholder "size" (`visualChecklist.ts:112`). Legacy has no such field; its Inlets group has "Moulded Bends Diameter". Jono thinks the process measures an effective length and will confirm. Once he does, relabel the field with units, or replace it.

**3.5 Shell condition under Claw? (F4). ❓ Maria.** The Claw tab is `visualChecklist.ts:141-146` and shell condition is `:166`. No change until decided.

**3.6 Vacuum pumps, regulator, releaser pump (F6).** Machine Configuration captures only pump count and lubrication (`MachineConfigStep.tsx:151-168`). Legacy page 2 (⏱ 9:07) has up to four rows each:
- Vacuum Pump Make / Model / Motor Size / "Does it Drive Milk Pump" / Regulator Type.
- Releaser Pump Make / Model / Motor Size.

Plan:
- Catalogue data is already bundled but unused: `Client/reference/vacuumPumps.json` and `milkPumps.json` (typed in `Client/reference/standardsData.ts:27-61`). There is no regulator catalogue yet.
- Follow the pulsator pattern end to end:
  - Entity: `Domain/Entities/EquipmentItem.cs:10-32`
  - Seed: `Data/Seed.cs:142-184` and `Data/SeedData/*.json`
  - Admin: `Pages/Admin/Equipment/*`
  - API: `Api/EquipmentController.cs:22-33`
  - Device sync: `Client/standards/equipmentSync.ts:14-32` → `Client/reference/lookups.ts:27-41`
  - UI: `MachineConfigStep.tsx:177-196`
  - "Other" is free text (`Client/ui/Combobox.tsx:143-146`).
- New config fields touch:
  - `Client/wizard/types.ts` (plus defaults at `:95-126`)
  - `Domain/Entities/MachineConfiguration.cs`
  - the sync DTO `Api/SyncController.cs:31-38` (plus mapping at `:278`, `:298`), added as **optional** fields so queued pushes from older clients still sync
  - an EF migration
  - `CONFIG_LABELS` (`amendments.ts:23-52`); the build fails if one is missing
  - the report config table (`testSummaryPdf.ts:107-121`)
- Model pumps as a list driven by the pump count, not as numbered columns.
- Then show the OEM capacity beside 8a. Jono (⏱ 14:50–15:18): "put the spec there … and then here we're putting in what we tested". The legacy pump table (min/max rpm, airflow per rpm) is extracted but not wired up (`standardsData.ts:27-38`, `plans/reference/standards-audit.md:85`).

---

## Phase 4 — wording and report (F13, F19)

**4.1 Oil vs water (F13).** A single item, `vp.reservoirHeight` (`visualChecklist.ts:30`), offers "Oil/Water Too Low / Too High / Level Incorrect". "Too Low" maps to "Raise level to 20 mm above the pump shaft centreline" (`Data/SeedData/faultObservations.json:14-31`, category `VPWaterReservoir`; offline copy `Client/reference/faultRatings.ts:105-109`).

That wording is only right for liquid-ring pumps (⏱ 24:14–24:55). Nothing reads `pumpLubrication` yet; the intent is only documented (`MachineConfiguration.cs:15`).
- Split the observations into oil and water variants with their own recommendations.
- Filter the choices by the pump lubrication already captured in step 2: Oil lubricated → oil; Liquid ring → water; Other → both. That answers Josh's "is it oil or water?" box without asking the tester again.
- Do the same for "Oil / water condition" (`visualChecklist.ts:29-32`) and the running oil/water section (`:283-292`).
- ❓ Maria: the oil wording (Jono suggested "top up oil"). Wording ships via the seed data and the admin Fault Observations page (`Pages/Admin/FaultObservations/*`).

**4.2 Report page 1 (F19).** The content is one flow with no page breaks (`testSummaryPdf.ts:290-315`). Josh's direction (⏱ 40:50) is to lead with what the farmer needs to act on.
- Page 1: farm and test details; fault summary and recommendations; general comments (0.7); Next Test Date (not implemented yet, `plans/build-checklist.md:77-78`).
- After a page break: machine configuration, numerical results, pulsator and cluster tables, visual checks, attachments.
- ❓ Josh: audit log at the front or the back? Amendment history is already its own page at the end (`testSummaryPdf.ts:319-366`). Josh's idea is a marker beside amended values ("see audit log") rather than leading with the changes.
- 🔎 Two things on Jono's report:
  - The "Visual checks" heading was stranded at the foot of page 2; it needs keep-with-next.
  - The page count ("2 / 3") ignores the appended pulsation pages, because pdf-lib appends them after pdfmake has numbered the pages (`:446-455`).

---

## Phase 5 — connectivity check (F21)

Jono had trouble connecting in the installed app and hotspotted off his phone; he isn't sure it was related (⏱ 2:15). Josh: "I'll do another check on it. Offline piece."

What the code does today (these are the known gaps in `plans/offline-tester-app.md` §1):
- The installed app starts at `/` (`wwwroot/manifest.webmanifest`), and the service worker never caches page HTML (`wwwroot/sw.js:34-67`, `:142-149`). Any navigation needs a connection.
- The online check pings `/health` every 30 s with a 5 s timeout (`Client/connectivity.ts:9-57`).
- Sync never retries on its own (`Client/sync/syncClient.ts:187-215`).

Steps:
1. Ask Jono what he saw: the message, the page, and whether he was on Wi-Fi or cellular.
2. Check App Insights for his account on Thu 11 Sep: `/health`, login and sync failures, and Azure SQL resume (cf. `4291b39`).
3. Try the installed app on a weak or captive network.

Also check (unconfirmed): straight after "Mark as complete & sync", the sign-off header read "sync: local-only" while the toast said "synced (1 pushed, 215 pulled)" (⏱ 39:35). The label shows `test.syncState` (`Client/wizard/ReviewSignOffStep.tsx:171`), and `runSync` re-reads the test before showing the toast (`WizardApp.tsx:332-343`). It may just be screen timing.

---

## Open questions

| Question | Owner | Blocks |
|---|---|---|
| "Airline bends size": bend size, or effective length of bends? | Jono | 3.4 |
| Faulty pulsators: show only on failed 14/15 readings, or also allow a manual "add"? | Jono | 2.3 |
| 14d limit: legacy passes 790 L/min for 19 pulsators; the app allows 30 per 10 units | Jono / NZMPTA | 14d verdict |
| Ratio spread (≤ 5%): the app judges the analyser's highest − lowest **pooled across front and back quarters**, as legacy's "Ratio Range Highest / Lowest" did. The standards note in `plans/reference/machine-types-tests-standards.md` reads it per quarter group (front vs front, back vs back — a plant can run different front/back ratios by design, and would fail pooled). Which does NZMPTA apply? Per group would mean capturing four ratio extremes instead of two | Jono / NZMPTA | ratio spread verdict |
| 12b with vented liners: ≤ 35 × clusters? Any minimum? | NZMPTA | 12b vented rule |
| Should the ISO 16 final checks (16a vs 1a, 16b vs 2a) be captured? Legacy has them; the app doesn't | Jono / NZMPTA | — |
| Cleaning reserve (2h): on Jono's test legacy showed **2064** while the app's formula gives 2118 at 1a = 41.5 (2136 at 41). Same milkline, same plant — the two disagree on the vacuum term (legacy's figure matches the app's formula at v = 43). Which is right per manual p43? | NZMPTA / Josh | 2h verdict |
| Shell condition / port condition: under Claw or under Shell? | Maria | 3.5 |
| Oil vs water recommendation wording | Maria | 4.1 |
| Step structure and renumbering | Josh | 2.1 |
| Audit log placement on the report | Josh | 4.2 |

## Found while preparing this plan

- **Effective-reserve open questions.** `plans/reference/standards-audit.md:105-114` asks which of two legacy tables is right (Q1) and whether the formula above 80 clusters holds (Q2).
  - Jono's standards sheet on screen (⏱ 35:30, "Air Flow Tables: Effective Reserve and Cluster Air Admission") lists 2 → 260, 4 → 320, 10 → 500, 20 → 600, 30 → 850, and "above 80 clusters = 2100 + ((n − 80) × 25)".
  - That matches the `EffectiveArea` table the app uses (Q1) and the app's formula (Q2).
  - It's still worth a check against manual p42, but it is evidence that both current choices are right.
- **ISO 16 is missing.** Legacy has "16 Final Check Tests" (16a working vacuum vs 1a, 16b effective reserve vs 2a; ⏱ 16:30). `standards.ts` has no section 16.
- **15b is mislabelled.** "Test pulsation reading (15b)" (`standards.ts:502`) is legacy's "Pulsator Airline Drop (1a − 15a)".
- **Buttons with no faults.** Claw inlet and outlet diameter and long milk tube diameter have neither `data` nor a fault `lookup`, so they render OK/Fault with no fault list (`visualChecklist.ts:145-146,199`).
- **No tests were run.** Client `node_modules` isn't installed, and `dotnet test` needs packages that aren't cached. Adding this file is the only change made to the repo.

## Verification

- Unit tests:
  - each formula, including "missing input → blank"
  - `DecimalInput` key sequences (`4 1 . 5`, `- 0 . 5`, `41,5`)
  - 12b limits: 10 clusters → 40–120; 37 clusters → 148–444
  - the show-on-fail step filter
- Replay Jono's test as a fixture, using the values from the legacy screens (⏱ 11:30–16:30, 33:45). Every calculated value should match what legacy showed:

  | Inputs | Expect |
  |---|---|
  | 1a 41.5, 1b 45 | 1c −3.5 ✗ |
  | 2a 3440, 2b 3440, 2c 3440, 2e 3440 | 2d 0, 2f 0 |
  | 5a 41.5 | 5b 0 |
  | Air flow start 4480, 10a 4400, 10c 4320 | 10b 80, 10d 80 |
  | 12a 3850, 37 clusters | 12b 470 ✗ (148–444) |
  | 14a 3850, 14c 3060, 14e 3050 | 14b 0, 14d 790, 14f 10 |
  | 15a 40 | 15b 1.5 |

- Run the formula check over the migrated legacy data (Phase 1).
- Jono re-runs a real test on the same device after Phase 0, and again after Phase 1.

*Evidence: the Teams recording and its `.vtt` transcript. Frames and a cleaned transcript were extracted to a temp folder for this plan; the timestamps above point into the original recording.*
