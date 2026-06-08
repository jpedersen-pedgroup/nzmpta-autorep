# Test workflow & fault ratings — wizard build reference

> Source: two NZMPTA docs (OneDrive `Clients/NZMPTA/`), provided 8 Jun 2026 to flesh out the wizard:
> - `ISO flowchart A3_FINAL.pdf` — the ISO test workflow testers follow ("Test Report Part A" + "Visual Check Part 2")
> - `CMM Ratings V2 Nov 25.xlsx` → `cmm-fault-ratings.csv` (this folder) — fault catalog: recommendations + severity
>
> Combine with the legacy `Autorep_bak` schema (`MMTestRecords1-3`, `MMAdditionalTR`, `PulsationSystemResult`, `IndividualClusterAirflow`, `VisualFaultsMMStart/Running*`).

## A. ISO numerical test workflow — the 15 ordered groups
From the flowchart legend. **Short test** = the red-highlighted items only, used when ISO ports are unavailable.

| # | ISO group | Key readings (flowchart refs) | Legacy table |
|---|---|---|---|
| 1 | Measure System Vacuum Levels | 1a working vac @receiver, 1b nominal, 1c regulation deviation, 1d @regulator, 1e @pump, 1f min VSD-speed vac | MMTestRecords1 (VL*) |
| 2 | Check Reserve Characteristics | 2a effective reserve, 2b airflow, 2c manual reserve, 2d regulation loss, 2e/2f regulator leakage, 2g required eff. reserve, 2h required cleaning reserve | MMTestRecords1 (RC*) |
| 3 | Check Regulation Characteristics | 3a avg receiver vac … 3f fall-off, 3g undershoot, 3h overshoot | MMTestRecords1 (RC regulation*) |
| 4 | Measure Vacuum Drop in Airline | 4a-4e (receiver/regulator/pump drops) | MMTestRecords1 (AVD*) |
| 5 | Test Regulator Sensitivity | 5a working vac in milk system, 5b sensitivity | MMTestRecords* |
| 6 | Reserve (Vacuum off Cluster) — **OPTIONAL** | 6a, 6b | MMTestRecords* |
| 7 | Check Vacuum Gauge Accuracy | 7a-7i (farm vs test gauge error at points) | MMTestRecords* |
| 8 | Test Vacuum Pump(s) | 8a capacity @50kPa (pumps 1-4), 8b min speed, 8c pump speed @50kPa | TestVaccumPumpDetails / MMTestRecords* |
| 9 | Vacuum Pump Exhaust Pressure | 9a; + oil/water flow rate | MMTestRecords* |
| 10 | Test Airline & Milk System Leakage | 10a-10d | MMAdditionalTR |
| 11 | Automatic Cluster Removers (**if present**) | 11a, 11b | MMAdditionalTR |
| 12 | Cluster Air Admission | 12a, 12b | MMAdditionalTR |
| 13 | Individual Cluster Air Admission — **OPTIONAL** | 13a-13c per cluster | IndividualClusterAirflow |
| 14 | Pulsator & Ancillary Equipment | 14a-14f (milk-sys leak, pulsator consumption, vacuum-sys leak) | MMAdditionalTR |
| 15 | Test Pulsation | 15a max pulsation chamber vac (B phase), 15b | PulsationSystemResult |

After 15 → **"Go to Additional Tests Flowchart"** (teat spray, milk meters, releaser-pump heads, regulator load, etc. — the remaining `MMAdditionalTR` fields). **⚠️ We have not yet seen that second flowchart — request it from NZMPTA.**

## B. Decision branches the Wizard Step Resolver must encode
- **ISO ports available?** (A1/A2/A3 air-flow; Vm/Vr/Vp vacuum) → No → install ports / **short test** (red items only) / discontinue.
- **Start gate:** vacuum `< 55 kPa`.
- **VSD (variable speed drive)?** → min-speed-vacuum (1f, 8b) + regulator-vs-VSD-transducer reading paths.
- **Pulsator stop system?** → deactivate/reconnect pulsators path (affects groups 6, 8).
- **Reserve vacuum off cluster (group 6)?** optional.
- **ACRs present (group 11)?** conditional.
- **Test all clusters individually / group test (group 13)?** optional → IndividualClusterAirflow.
- **Vacuum pump liquid-ring vs oil-lubricated (group 9)** → oil/water flow-rate check.
- **Milking position vs vacuum-off-cluster** is called out per group → capture a position flag per reading set.

> **Numbering note:** the PRD describes "Vacuum & Reserve sections 1–7" + "Additional Tests 8–16"; the ISO flowchart uses 15 groups; the legacy app has its own sectioning. These are the same tests grouped differently — **reconcile the exact wizard step grouping with NZMPTA / the legacy UI** before finalising step boundaries. The ISO groups above are authoritative for *content & order*.

## C. Fault catalog → domain model (`cmm-fault-ratings.csv` — 113 faults / 13 components)
Components (count): Vacuum Gauge 6, Regulator 9, Vacuum and Measurement Regulators 7, Main/Receiver Airline 2, Pulsation 13, Cluster 11, Moving Parts 1, Stationary Parts 3, Vacuum Pump 16, Pipe lines 22, Releaser 17, Rubberware 2, Misc 4.

- **StandardRecommendationWording** seed = the `Recommendation` column, keyed by `Component` (+ the `Fault` it addresses). Admin-CRUD per PRD; this is the initial seed.
- **Fault.Severity** = `Rating` → enum `{Critical, Major, Minor}`. Source casing varies ("minor"/"Minor") → normalise. **3 Critical, 59 Major, 45 Minor.**
- **Conditional ratings (6 rows)** need a rule confirmed with NZMPTA — e.g. "Minor / Major", "Major/minor" (severity depends on whether it affects pulsation/cleaning/overload), and one **escalation**: *"Major, moving to Critical if not actioned within a month"* → model an optional escalation note/period on Fault.
- Replaces the legacy **group-level** severity with **per-fault** severity (PRD US 23 & 41).
- `Component` maps onto wizard sections (Vacuum Pump → config/pump tests; Pulsation → pulsation step; Cluster → cluster steps; Pipe lines/Releaser → visual + additional; Moving/Stationary Parts → visual-faults safety items; etc.).

## D. Legacy AutoRep Plus screen structure (the canonical tester UX)
Source: `Autorep Plus Instructions 150724.pdf` + NZMPTA training video (`youtu.be/JNQ-vHZ09Js`). This is the flow testers know today — the strongest guide to wizard step **grouping**.

**Left-hand menu = the step list; the tester can jump to any form (non-linear). "Save" on a form opens the next.** Seven forms:
1. **Farm & Milking Machine** — TWO pages. *Pg 1:* equipment calibration-expiry dates, farm owner (mandatory), dairy company, supply number, **plant type** (highline herringbone / lowline / rotary — mandatory), **plant size = cluster number** (mandatory; drives all standards), pulsator model + count, ACRs y/n, bail gates y/n, milk meters y/n, liners vented?, **flushing pulsation system** (→ cleaning-reserve vs effective-reserve), milkline size. *Pg 2:* vacuum pump make/model/motor, drives-milk-pump?, regulator type, releaser/milk-pump make/type/motor, **other ancillary equipment** (milk-system & vacuum-system lists + "Other" + count → standard air consumption). "Next" between pages → an "is the cluster number right?" confirm.
2. **Visual Faults** — checklist. **Tick/cross/blank by click-cycling** (1 click = tick/OK, 2 = cross/fault, 3 = blank). Per-item **dropdown of standard fault wording** (overwritable). Some fields are **yellow data-capture** (not faults) e.g. long-pulse-tube size, pulsator run time.
3. **Test Record** — ONE form "split into groups of tests": vacuum & airflow + vacuum-pump readings. **Live tick/cross** (working vacuum → auto-calcs regulation deviation; effective reserve → shows required standard effective reserve + cleaning reserve). ≈ ISO groups 1–9.
4. **Additional Tests** — only sections relevant to the machine config; add unlisted equipment (manual tick/cross); cluster fall-off, peak regulator load (manual). ≈ ISO 10–12 + extras.
5. **Pulsator Test Results** — faulty rows only (create N) or all. Required: fastest/slowest rate + highest/lowest ratio (checks ≤6 ppm spread, ≤5% ratio variation + per-model standard). ≈ ISO 14–15.
6. **Individual Cluster Tests** — optional; faulty clusters only or all. ≈ ISO 13.
7. **Fault Summary** — all faults carried forward, **grouped**; tester writes recommendations + a rating **per group** ("highest rating in the group"). → **PRD changes this to per-fault severity.**
Then → **Reports** screen (per-form printable PDFs).

Other behaviours: **"Download my tests"** (top-right) pulls ALL the tester's synced tests back to a device (confirms the offline reprint/migration requirement — see [[legacy-test-reprint-offline-sync]]). Company Admin/Management can **view but not edit** any tester's tests online.

**Grouping takeaway:** legacy is a **coarse ~7-form flow with a jump-anywhere left menu** = tester muscle memory. **Recommend mirroring it** (≈9–10 wizard steps incl. the PRD's visual pre-start/running split + a sign-off step), with **Test Record as one step holding collapsible ISO sub-sections + live pass/fail**, and keep the **left-nav jump** (also satisfies the PRD's "back-navigate to any step"). This is closer to the mockup's "grouped" option than "granular".
