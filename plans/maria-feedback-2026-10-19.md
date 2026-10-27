# Maria's feedback and report changes, 19 Oct 2026 — update plan

> **Sources:**
> - **E1:** Maria's 23-point reply on the 27 Aug guides thread (received ~18 Oct). Cited as `E1.n`.
> - **E2:** Maria's forward of her 8 Oct email to the AutoRep sub-committee, with the sub-committee's answers inline and her own notes on the Test Summary page. Cited as `E2`.
> - **Josh's lists:** the "report changes" and "general changes" lists, 19 Oct.
>
> **Assessed against:** `main` @ `63b5282` (#87 merged). #88 (NZMPTA → MPNZ rebrand) merged on 19 Oct, after this was assessed, so some line numbers may have shifted. Code claims cite a file:line that was read.
> **Paths** are relative to `src/Autorep.Web/` unless they start with `tests/`, `tools/` or `plans/`.
> **Legend:**
> - ✅ already done, so just tell Maria
> - 🔨 build
> - ❓ needs an answer (owner named)
> - 💬 answer-only

---

## Josh's decisions (19 Oct)

| Topic | Decision |
|---|---|
| Next Test Due | Smaller, at the **bottom of the TEST box** (under calibration), shown as MM/YY |
| New farms | **Keep company-admin review.** Add a dismissible admin banner that first shows 48 h after a farm goes under review. It stays until dismissed or approved |
| Multiple PDFs | **Several pulsation analyser PDFs** per test (not arbitrary documents) |
| OEM Specs | **Build the spec editor**, not just the rename |
| Phone numbers | **A data check, not a code change.** Count testers per phone number; the stored number is probably the company's |
| Regions | Still **selectable on farm forms**. Remove the Regions admin page from navigation, and drop Region from the report |
| Amendment history | Fix commentary is **per fault**. "Fixed" shows only in the history |
| Moving a tester | **Farms stay behind** with the old company. Add a clear "Change company" button with a confirmation |
| Expired licence | **Doesn't stop testing.** An expired tester keeps full use of the app: sign in, reset password, test, add farms, sync. The report highlights a licence that had expired by the test date |

---

## Status of every item

| # | Item | Status | Workstream |
|---|---|---|---|
| E1.1 | Calibration dates amber/red on PDF | ✅ The PDF already prints them plain (label `#64748b`, dates `#1e293b`, `Client/report/testSummaryPdf.ts:514-527`). Amber/red appear only in the app (`Client/calibration/status.ts`), which is what the guide describes | 💬 |
| E1.2 | Tester, not company admin, approves farms | Josh decided to keep admin review, plus a banner | C3 |
| E1.3 | testing@milkingpumping.co.nz for reset and farm-review emails; "how with RocketSpark?" | 💬 Not RocketSpark: the domain's mail runs on Microsoft 365. 🔨 retarget the mailbox plan | H |
| E1.4 | Guides NZMPTA/MPTA → MPNZ | 🔨 Word sources in OneDrive | I |
| E1.5 | Why re-accept terms after licence renewal? | 🔨 Drop the licence-change trigger (❓ confirm with Maria) | B |
| E1.6 | Expired tester can reset password and view previous tests | 🔨 Today they get a "finish sending" screen only. Josh: expiry blocks nothing; the report highlights it | B, A |
| E1.7 | Licence warning from 60 days, red, new wording | 🔨 | B |
| E1.8 | Tests from other devices visible? | ✅ All of a tester's own tests sync to any device they sign in on (after **Sync now**) | 💬 |
| E1.9 | Guide wording "milk-company logo" → "dairy supply number"; farm search fields | 💬 Search covers farm name, supply number and town. 🔨 add owner/operator name | D, I |
| E1.10 | Remove "by your testing company and NZMPTA" | 🔨 guide | I |
| E1.11 | What does the "no ISO test ports → short test" statement mean? | 💬 Today it only shows a banner and a report label; nothing is skipped. ❓ Maria: what should a short test skip? | D |
| E1.12 | "NZMPTA equipment catalogues" → "… Testing Standards Manual" | 🔨 guide (MPNZ after the rebrand) | I |
| E1.13 | "Guards installed on pulsators" singled out | 🔨 Move it into the Pulsation checklist | D |
| E1.14 | Summary: tester name, registration no. + expiry, Next Test Due MM/YY | ✅ Name, company, registration number and expiry are already in "Tested by" (`testSummaryPdf.ts:342-355`). 🔨 larger, and MM/YY | A |
| E1.15 | Company tests and other-device tests offline | ✅ own tests are cached offline. Company tests are online only by design | 💬 (new feature if wanted) |
| E1.16 | Visual faults: "Test ports in place Y/N" | 🔨 ❓ Maria: which section, and the rating for "No" | D |
| E1.17/19/21 | Testers → Users, "+ New User", "Create User" | 🔨 | C1 |
| E1.18 | Company logo on report pages | ✅ Page-one letterhead and every running header (#58/#59) | 💬 |
| E1.20 | Company Administrators must have a company | 🔨 Today only Testers must | C1 |
| E1.22 | Default recommendations in super admin guide 5.3 | 💬 123 of 186 fault observations have none. ❓ Maria to supply wording, or edit them in admin | J |
| E1.23 | Milk Supply Companies → Dairy Processing Companies | 🔨 labels only | C1 |
| E2 | 14d ≤ 35 L/min per cluster, no minimum | ✅ #65 (`Client/passfail/standards.ts:584-590`) | — |
| E2 | 12b vented liners 4–35 × clusters | ✅ #65 (`standards.ts:408-412`) | — |
| E2 | Ratio spread "front to back, ±5%" | ❓ Conflicts with Jono (23 Sep: pooled highest − lowest, which is what the app does) | D |
| E2 | Shell stays under Shell | ✅ no change. Closes F4 / 3.5 | — |
| E2 | Oil pump wording "Top up oil" | 🔨 Oil/water split (tester-feedback 4.1) | D |
| E2 | Cleaning reserve (2h): π? | 💬 **Not π.** ❓ which vacuum. See D5 | D |
| E2 | ISO 16a/16b final checks: include | 🔨 ❓ limits | D |
| E2 | "Airline bends size" → main airline diameter and length | 🔨 | D |
| E2 | Next Test Due less prominent | 🔨 | A |
| E2 | Remove Completed date/time | 🔨 | A |
| E2 | Remove Region and RAPID from farm details | 🔨 | A |
| E2 | Farm name field + owner/operator + owner email | 🔨 labels and report. The data already exists | A |
| E2 | Larger registration number and expiry | 🔨 | A |
| E2 | Summary doesn't show "regulator not big enough" | 🔨 add the regulator "No" and pulsator band fails | A |
| E2 | Couldn't open the PDFs in her reader (Nitro Pro) | 🔨 hardening. ❓ Maria: Nitro version and the error | A |
| Josh | Text a little bigger overall | 🔨 | A |
| Josh | "Tested Date" → "Date of Test" | 🔨 | A |
| Josh | "Well done – No faults recorded" | 🔨 wording | A |
| Josh | Amendment history: new rating, fix commentary, "Fixed" | 🔨 | E |
| Josh | Licence expiry emails 60/30 days | 🔨 ❓ Maria's text. ❓ scope (PRD O4) | H |
| Josh | New-farm banner, 48 h delay | 🔨 admin banner | C3 |
| Josh | Multiple PDFs appended | 🔨 several analyser PDFs | F |
| Josh | Old tests: "…AutoRep Plus (prior to xxxxx)" | 🔨 ❓ cutoff date | C4 |
| Josh | Nitro Pro | see E2 | A |
| Josh | Phone numbers personal vs company | 🔨 data check | J |
| Josh | Super admin changes tester's company | ✅ dropdown exists. 🔨 button and farm scope | C2 |
| Josh | Hide Regions for admin, not on report | 🔨 | C1, A |
| Josh | OEM Specs / where to edit specs | 🔨 spec editor | G |

---

## Workstreams

Each workstream is roughly one PR. A, E and F all edit `Client/report/testSummaryPdf.ts`, so they go in that order. A, B, C and G touch mostly separate files and can run in parallel. B and C1/C3 both touch `Pages/Shared/_Layout.cshtml`, which means small merge conflicts at most.

**Before anything:** #88 (rebrand) is merged, so build on current `main`. All new wording says MPNZ.

**Who owns which files** (they run in parallel):
- **A** owns the report: `Client/report/*` and `Client/faults/buildFaults.ts`. That includes the report's "Dairy processor" and "Owner / operator" labels.
- **C** owns the admin pages and the label renames in the tester app (`NewTestApp.tsx`, `WizardSteps.tsx`). That covers both "Owner / operator" (A5) and "Dairy processing company" (C1).

### A. Report page one (Test Summary PDF)

All in `Client/report/testSummaryPdf.ts` unless noted. Tests: `testSummaryPdf.test.ts`, `testSummaryPdf.bytes.test.ts`.

1. **Next Test Due.**
   - Move `nextTestBlock` (`:487-506`) and the annual-test note (`:507`) from the top of the TEST panel to below the calibration table (`:513-527`).
   - Use about 9 pt and format the date as MM/YY. The "proposed" tag stays on drafts.
   - Update the order test (`testSummaryPdf.test.ts:599-607`).
2. **Date of Test.**
   - The title line `Tested {date}` (`:463`) becomes `Date of Test {date}`.
   - On version 2 or later, print the *original* test date via `originalCompletedAt(test.amendments)` (`nextTestDate.ts:44`). A tester's amendment overwrites `markedCompleteAt` today, so the date would otherwise drift.
   - Remove the TEST panel's `Completed` field (`:512`). The amendment history keeps its own dates.
3. **Text a little bigger.**
   - There's no central style today: 69 hard-coded `fontSize`s, with `defaultStyle` holding only colour (`:932`).
   - Add a `size()` helper or a style table, and raise body text by about 1 pt (≈10%). Raise headings proportionally. Leave the fixed footer and disclaimer bands (`:132-134`, `:414`) alone, since they're absolutely positioned.
   - Re-check the page-break thresholds (`:1017-1019`) and the laid-out page-count tests (`testSummaryPdf.test.ts:802-820`).
   - Regenerate both sample PDFs and look at them.
4. **Registration number and expiry larger.** `testedByBlock` (`:342-355`): the registration line goes from 7.5 pt muted to the same size and colour as the other lines.
   - **Expired licence highlighted:** when the stamped expiry (`testedBy.registrationExpiry`) is before the test date, print it in red with "expired" (e.g. "expired 05 Oct 2026"). Migrated tests use their recorded sign-off expiry. Testing isn't blocked (B2).
5. **Farm panel** (`:475-486`).
   - Remove `Region` and `RAPID number`.
   - Label the headline "Farm name".
   - Rename `Farmer` to "Owner / operator" and add their email (`farm.contactEmail`, already stored, `Domain/Entities/Farm.cs:42`).
   - The same labels in the admin and tester app are renamed under C1. No schema change.
6. **"Well done – No faults recorded".** Reword `resultBanner` (`:545-552`, currently "No faults found" / "No faults recorded — the machine passed every completed check."). Migrated tests print their own line (`:1109`). ❓ Maria: should those say it too?
7. **Summary fault list includes everything that failed.**
   - `buildFaultInputs` (`Client/faults/buildFaults.ts:12-53`) already includes failed readings (12b and others).
   - Add the regulator "correct and big enough for this plant?" = **No** (now only red in the page-two table, `:657-665`) and pulsator model band FAILs (`:729-740`).
   - Otherwise a "Well done" banner can print beside a red result.
   - ❓ Maria: rating and recommendation wording for the regulator. Default: Major.
8. **PDF compatibility (Nitro Pro).**
   - The two samples Maria couldn't open are pdfmake 0.3.10 output: `%PDF-1.3` with a valid classic xref (checked 19 Oct) and embedded Roboto.
   - They use transparency (swirl opacity, logo soft mask), which is a PDF 1.4 feature inside a file declared 1.3.
   - A report with an analyser PDF attached is rewritten by pdf-lib as `%PDF-1.7` with object streams and an xref stream (`:1263-1266`).
   - Hardening:
     - set `version: "1.7"` in the doc definition (`:926-1022`)
     - save merged files with `useObjectStreams: false`
     - then check both kinds of file with `qpdf --check` and in Adobe Reader and a Nitro Pro trial
   - ❓ Maria: Nitro version, the exact error, and whether her copy was the plain report or the one with an analyser PDF attached. "Universal" may mean PDF/A. pdfkit can write PDF/A-2b, but the merge step would break conformance, so only go there if MPNZ needs archival PDF/A.

### B. Licence: warning, access when expired, terms

1. **Warning from 60 days, red, new wording** (E1.7).
   - Device: `Client/shell/licenceBanner.ts:30-52`. Server copy: `Pages/Shared/_Layout.cshtml:12-22,102-124`.
   - Drop the 61–180-day "Plan your renewal" tier.
   - Within 60 days: "Your Tester licence expires on 05 Oct 2026 — email six tests to info@milkingpumping.co.nz by this date." Expired text gets MPNZ wording.
   - Use the NZ date on both. The server uses UTC today, while admin pages use `Domain/NzTime.cs:23`.
   - Make the expiry day itself still valid, matching enforcement (`Domain/LicenceScope.cs:24-29` uses `<`). The banners say "expired" on that day today.
   - Admin Testers list badge: amber from 60 days, not `AddMonths(2)` (`Pages/Admin/Testers/Index.cshtml:72-77`).
2. **An expired licence blocks nothing** (E1.6; Josh, 19 Oct: "an expired licence doesn't stop testing, it just highlights it on the report"). Remove the sync-only mode:
   - Forgot-password stops skipping lapsed testers (`Pages/Account/ForgotPassword.cshtml.cs:58-74`). Deactivated accounts still get nothing.
   - Cookie sign-in gives a normal session (`Services/SignInGates.cs:47-53`, `Services/TesterClaimsPrincipalFactory.cs:35-38`, `licence-expired-sync-only` audit).
   - JWT login and refresh stop refusing (`Api/AuthController.cs:74-83,123-132`).
   - Farm creation stops refusing (`Api/FarmsController.cs:144-145`).
   - The device stops routing a lapsed identity to the send-only screen (`Client/db/identity.ts:139-148`, `Client/shell/shell.tsx:114-128`).
   - Then retire `/Account/FinishSync`, `SyncOnlyApp.tsx` and the sync-only policy (`Program.cs:207-209`), or leave them unreachable. `Domain/LicenceScope.cs` can go once nothing calls it.
   - The banner (B1) is the only in-app signal. The report highlights it (A4).
   - Tests: drop or invert the sync-only E2E and auth tests.
3. **No terms re-acceptance on renewal** (E1.5). Remove the `TermsAcceptedLicenceExpiry != LicenceExpiryDate` condition (`Services/SignInGates.cs:55-65`); keep the terms-version one. ❓ Confirm Maria doesn't want it (her question reads as "why?").
4. Fix wording that says expired testers "can't sign in" or are limited (`Pages/Admin/Testers/Edit.cshtml:80`, `Domain/Entities/Tester.cs:16-17`, `UBIQUITOUS_LANGUAGE.md:85-86`).
5. 🔎 Reset links last Identity's default **1 day**, but the emails say 1 hour (`Program.cs:81`, `ForgotPassword.cshtml.cs:45-53`, `Pages/Admin/Testers/Edit.cshtml:135`). Set `DataProtectionTokenProviderOptions.TokenLifespan` to 1 hour, or change the wording.

### C. Admin portal

**C1. Labels, rules, Regions.**
- **Testers → Users:**
  - nav `Pages/Shared/_Layout.cshtml:81`
  - `Pages/Admin/Testers/Index.cshtml:5,11,15,24` ("+ New User", "Add a user")
  - `Pages/Admin/Testers/New.cshtml:5,10,65` ("New user", "Create User")
  - The URL stays `/Admin/Testers`.
- **Company Administrators must have a company:**
  - validate in `Pages/Admin/Testers/New.cshtml.cs:52-53` and `Edit.cshtml.cs:72-73`
  - fix the hint "admins may be unassigned" (`New.cshtml:44`)
  - Super Admins stay company-less.
  - Check staging for existing company-less Company Admins before shipping.
- **Milk Supply Companies → Dairy Processing Companies,** text only:
  - nav `_Layout.cshtml:84`
  - `Pages/Admin/MilkSupplyCompanies/{Index,New,Edit}.cshtml` and their error messages
  - Farm Edit `:62` and Farms list `:42`
  - tester app `Client/ui/NewTestApp.tsx:267,270,476` and `Api/FarmsController.cs:180`
  - wizard `WizardSteps.tsx:242`
  - the report's `testSummaryPdf.ts:481` label is A's ("Dairy processor")
  - Class names and routes stay.
- **Owner / operator** (from A5): rename "Farmer name" / "Farmer" in admin Farm Edit (`Pages/Admin/Farms/Edit.cshtml:95-112`), the add-farm modal (`Client/ui/NewTestApp.tsx:315-324`) and the wizard Setup card (`Client/wizard/WizardSteps.tsx:240-248`).
- **Regions:**
  - Remove the Regions link from the super admin nav (`_Layout.cshtml:83`). The page stays reachable by URL, but nobody needs it.
  - Region stays selectable on farm forms (admin Farm Edit `:84-89`, tester add-farm modal `NewTestApp.tsx:486-502`).
  - It leaves the report (A5).

**C2. Change a tester's company.**
- Add a "Change company" button on the tester's page with a confirmation explaining what moves. Today it's a bare dropdown (`Pages/Admin/Testers/Edit.cshtml:63-68`).
- **Farm scope by the test's company, not the tester's current one:**
  - `Data/FarmScope.cs:17-19` joins each test's tester's *current* `TestingCompanyId`, so a moved tester takes the farms they tested with them. Use `MachineTest.TestingCompanyId` (stamped once, never re-stamped) instead.
  - The same join is in `Pages/Admin/Farms/Index.cshtml.cs:46-52` and `Farms/Edit.cshtml.cs:153-160,180-185`.
  - Align the counts in `Pages/Admin/Companies/Index.cshtml.cs:25` and the tester dropdown in `Pages/Admin/Tests/Index.cshtml.cs:86-88`.
- **Edge case:** the moved tester's device still holds old farm IDs. Testing one of those farms for the new company would make sync mint a duplicate (`Api/SyncController.cs:659-703`). Let sync accept a farm the tester has personally tested before; the new company then gains it through the new test.
- Tests: `CompanyTestVisibilityTests`, and farm scope tests for a moved tester.

**C3. New-farm review banner (admin).**
- A banner in the admin portal for the farm's Company Administrators (Super Admins for company-less farms, as `Services/FarmReviewNotifier.cs:103-116` does). It shows once a farm has been under review for 48 h (`Farm.PendingReviewSince`) and lists the farms with a link to each.
- It goes away when the farm is approved (`Pages/Admin/Farms/Edit.cshtml.cs:68-85`) or the admin dismisses it.
- Dismissals are stored server-side per user per farm, in a small new table. Nothing like that exists today (`Data/AutorepDbContext.cs:29-42`).
- The existing non-dismissible alerts stay (Farms list `:19-25`, home tile `Pages/Admin/Index.cshtml:34-37`).

**C4. Old-test banner.**
- Migrated tests are detected from the payload (`Services/PayloadUnits.cs:59-60`, `Client/sync/syncClient.ts:330-358`).
- Banner text becomes something like "Migrated from AutoRep Plus — tests before {cutoff date}":
  - admin viewer: `Client/wizard/VersionBanners.tsx:139`
  - the tester's own migrated test: `Client/wizard/WizardApp.tsx:865-868`, which uses the generic read-only text today
  - colleague view: `WizardApp.tsx:859-863`
- The cutoff is one app setting, since no cutover date is stored anywhere. ❓ Josh/Maria: the date (go-live).

### D. Checklist and standards

1. **Guards on pulsators** (E1.13).
   - Today it's a standalone checkbox above the Running tabs (`Client/wizard/VisualFaultsStep.tsx:188-198`, `LocalTest.guardsOnPulsators`). It raises no fault and doesn't print.
   - Make it an OK/Fault item in the Pulsation section (`Client/wizard/visualChecklist.ts:247-261`), with a "Guards not installed" observation in both seed files. Map old tests' `true` to OK.
2. **Test ports in place** (E1.16). A new OK/Fault item. Adding one means:
   - a lookup category in `Client/reference/faultObservations.json` and `Data/SeedData/faultObservations.json`, plus a fallback in `Client/reference/faultRatings.ts`
   - it counts toward completeness (`visualChecklist.ts:331-339`)
   - ❓ Maria: which section (Receiver? Regulator?), whether to show it only when ISO ports are available, and the rating for "No".
3. **Oil vs water wording** (tester-feedback 4.1). Split the `VPWaterReservoir` observations (`Data/SeedData/faultObservations.json:14-31`, `Client/reference/faultRatings.ts:105-109`) into oil and water variants. Oil "Too Low" → "Top up oil". Filter by `config.pumpLubrication`. Applies to `visualChecklist.ts:29-35` and the running section (`:283-292`).
   - The seed only loads into an empty table (`Data/Seed.cs:118`), so staging/prod need a small data migration, or Maria edits the rows on the Fault Observations page.
4. **ISO 16 final checks.** A new section: 16a (working vacuum vs 1a) and 16b (effective reserve vs 2a), in `Client/passfail/standards.ts`, the C# resolver, the report and fixtures. ❓ The pass limits for each (equal within what tolerance?).
5. **Cleaning reserve (2h)** — answer for the sub-committee:
   - π isn't the cause. 3.1416 vs 3.1415 moves the result by less than 0.1 L/min, and the app uses full-precision π.
   - The manual's formula (p43: 3.1415 × d²/4 × 8 × (100 − *cleaning vacuum*)/100 × 0.06, with d = ID = OD − 2) reproduces its own table exactly.
   - Legacy's **2064** is the table's 100 mm / **43 kPa** value. The app rounds Jono's 1a of 41.5 up to 42 (`standards.ts:34-47`) and gets **2100**, which is the table's 42 kPa value.
   - ❓ Sub-committee: is "cleaning vacuum" the working vacuum (1a) rounded up, or a separate reading taken while washing? If separate, add it as a reading.
6. **Main airline.** Replace "Airline bends size" (`ma.bendsSize`, `visualChecklist.ts:119`) with recorded measurements "Main airline diameter (mm)" and "Main airline length (m)". Keep old values readable in history. ❓ Confirm the units.
7. **Farm search by owner/operator name** (E1.9): `matchFarms` (`Client/ui/NewTestApp.tsx:26-31`) adds `farmerName`.
8. **Ratio spread.**
   - ❓ The sub-committee wrote "front to back with ±5% the limit". Jono said on 23 Sep to keep it pooled (highest − lowest across both sides, because the analyser reads two channels at a time), and that's what ships (`Client/passfail/derived.ts:91`, ≤ 5%).
   - If front-vs-back wins, capture front and back figures separately and judge the difference.
9. **Short test** (E1.11). Today `isShortTest` only shows a banner and the report's "No ISO ports (short test)" (`Client/wizard/wizardStepResolver.ts:103`, `WizardApp.tsx:842-846`). ❓ Maria: which tests should be skipped? Otherwise reword the guide.

### E. Amendment history: rating change, what was done, "Fixed"

- **Today:**
  - `AmendmentRecord` holds plain `FieldChange { section, label, from, to }` (`Client/db/testStore.ts:56-122`).
  - A fix shows as "Fault — Major: X" → "OK", and a reclass as "Fault — Major: X" → "Fault — Minor: X" (`Client/versioning/amendments.ts:256-263`).
  - Only admins write a reason, and testers write nothing (`WizardApp.tsx:584-586`).
- **Build:**
  - Add `faultOutcomes?: { key, label, fromSeverity, to: "Fixed" | FaultSeverity, note }[]` to `AmendmentRecord`.
  - When an amendment (the tester's own, or a super admin's) turns a base-version fault into OK, a lower rating, or a failed reading into a pass, ask for the outcome and "what was done" per fault before sign-off.
  - Print a "Fault changes" table in `buildAmendmentBlock` (`testSummaryPdf.ts:1027-1101`): Fault | Was | Now | What was done. Also show it in the admin audit drawer (`AuditPanel.tsx:191-228`).
  - **"Fixed" is not a `FaultSeverity`.** Adding it would break counting (`faultAggregator.ts:48` would print "1 fault needs attention" over 0/0/0 chips).
  - Server: `AdminVersioning.cs:155-162,199-200` passes new fields through. Make sure it keeps `faultOutcomes`.
  - Company Admins can't change faults (`AdminVersioning.cs:74-78`), so this is testers and super admins only.

### F. Several analyser PDFs per test

- **Today:**
  - One `LocalTest.pulsationPdf` (`Client/db/testStore.ts:30-46,161`), one append (`testSummaryPdf.ts:1251-1271`).
  - Upload takes `files[0]` (`ReviewSignOffStep.tsx:110-113,223-232`) and replaces the existing file (`WizardApp.tsx:512-535`, 15 MB cap).
  - The server reads a single object (`Services/PulsationPayload.cs:26-167`), and the GET endpoints return one file (`Api/SyncController.cs:193-216`, `Api/TestsController.cs:236-259`).
  - Blob keys are already per-sha (`Services/Pdfs/PdfKeys.cs:21-22`).
- **Build:**
  - `pulsationPdfs[]`, still reading the old single field for older devices and payloads.
  - Multi-select upload with remove-per-file; append in upload order.
  - Per-file GET by sha.
  - Amendment diff per file (`amendments.ts:267-271,354`) and merge handling (`merge.ts:73,124-125`).
  - Backfill and readers updated.
- **Limits need a decision:** PDFs travel inline as base64 in the sync push, under ASP.NET Core's default 30 MB request limit, and the Final Report is capped at 25 MB (`Services/FinalReportStore.cs:24`, `finalReportUpload.ts:35`, `AdminTestsController.cs:222`). Two 15 MB files already break both.
  - **Recommendation:** upload each PDF on its own request (multipart, not base64), keep 15 MB per file, allow up to 5, and raise the Final Report cap to fit (about 80 MB).

### G. OEM Specs (spec editor)

- **Today:**
  - `EquipmentItem` holds only type, name and brand (`Domain/Entities/EquipmentItem.cs:11-44`).
  - The specs are bundled JSON matched by **name**: `Client/reference/pulsatorBands.json` and `vacuumPumps.json` via `Client/reference/standardsData.ts:12-38,68-97`.
  - So a model added in admin gets no verdict, and **renaming an item silently breaks its spec match**.
  - The super admin guide calls changing specs "a software release".
- **Build:**
  1. **Rename** "Equipment catalog(ue)s" → "OEM Specs": `Pages/Admin/Index.cshtml:63-67`, `Pages/Admin/Equipment/{Index,New,Edit}.cshtml` and errors, the tester-facing "catalogue" hints (`Client/passfail/standards.ts:103,111-112`), `UBIQUITOUS_LANGUAGE.md:44`.
  2. **Spec fields** per type, as a JSON column on `EquipmentItem` with typed view models:
     - Pulsator: rate and ratio min/max, phase b/d, max chamber vacuum, recommended rate/ratio, max clusters per pulsator.
     - Vacuum pump: min/max rpm, air flow per rpm, motor size factor, water flow rate.
     - Start with these two, since they drive verdicts. Releaser sizing is keyed by clusters × heads, not by model, so it stays as is.
  3. **Seed** from the bundled JSON into existing items (match by name / make|model). Unmatched rows become new items.
  4. **Admin New/Edit** show the spec fields for the type, with validation. Audit is automatic (`Data/AuditInterceptor.cs`).
  5. **Sync**:
     - `Api/EquipmentController.cs:22-33` returns specs; `Client/standards/equipmentSync.ts` stores them.
     - `standardsData.ts` reads synced specs first and falls back to the bundled JSON offline.
     - New tests record the equipment item **id** beside the name, so renames are safe. Old tests fall back to name.
  6. **Snapshot** the spec a test was judged against at sign-off, the same way 2g/2h are stored readings. A later spec edit then doesn't change an old report. This closes the "Vendor Specification … snapshot-at-test-start" line (`plans/build-checklist.md:134,223`) without effective dating. ❓ Confirm effective dating isn't needed.
  - This is where Maria's pending spec updates (her 25 Aug email) go. She can enter them herself.
  - Still open, unchanged: 8 vs 11 equipment types (`plans/build-checklist.md:132,224`).

### H. Licence expiry emails (60 and 30 days)

- **Scope:** the PRD lists "O4 Proactive notification schedule ($2,500)" as out of scope, "add only by written variation" (`PRD.md:266,296`, `plans/build-checklist.md:201`). O4 was framed around Next Test Date emails. ❓ Josh: treat licence emails as a variation, or as part of this round?
- **Build:**
  - A daily `BackgroundService` (pattern: `Services/PulsationBackfill.cs:235-291`) runs at about 8 am NZ.
  - It records each send in a new `LicenceReminder` table, unique on (TesterId, LicenceExpiryDate, Threshold). That table is the double-send guard, since scale-out is planned and deploys overlap. A renewal starts a fresh cycle.
  - Catch-up rule: send the 60-day email when 31–60 days remain and it hasn't been sent; same for 30.
  - Skip: deactivated accounts, accounts with no email, licences already expired.
  - **Off by default** (`LicenceReminders:Enabled`), because staging holds real tester data.
  - A configured public base URL for links, since the job has no request (links come from `Request` today).
- **Text:** Maria is sending it. Store it as editable text for super admins (pattern: `PrivacyContent`, `Pages/Admin/Privacy`) so she can adjust it without a release.
- ❓ Maria: recipients. Tester only, or also the company's admins? Graph sending supports one To and no CC (`Services/GraphEmailSender.cs:41-44`), so CCs would mean separate sends or a sender change.
- **Mailbox (E1.3):**
  - Maria wants **testing@milkingpumping.co.nz**, not the planned noreply@.
  - milkingpumping.co.nz mail runs on Microsoft 365 in MPNZ's tenant (Exchange Online MX), so RocketSpark isn't involved.
  - Their M365 admin creates testing@ (a shared mailbox needs no licence) and follows the steps already written for MPNZ IT, with noreply@ replaced by testing@. Update `OneDrive …/Clients/NZMPTA/Email to MPNZ IT - AutoRep mailbox - 2026-10-19.html`.
  - Our cross-tenant sender stays on hold until they confirm.
  - Replies then land in a monitored inbox, which is a plus.

### I. User guides

- Word sources: `OneDrive - Pedersen Group/Clients/NZMPTA/NZMPTA AutoRep … Work Instructions.docx`. PDFs ship from `src/Autorep.Web/Guides/` (`Services/GuideCatalog.cs`).
- Do these **after** A–D, since several screenshots change.
- **All three guides:** NZMPTA/MPTA → MPNZ (E1.4).
- **Tester:**
  - p6 §2.3 reset note (E1.6), §2.4 banner (E1.7), terms on renewal (E1.5)
  - p12 §5.1.2 "its milk-company logo" → "the farm's dairy supply number" plus the search fields (E1.9)
  - p13 fig. 9 note (E1.10)
  - p14 §6.1 short test (E1.11)
  - p20 Tip → "MPNZ Milking Machine Testing Standards Manual" (E1.12)
  - p20 §7.3.3 guards (E1.13)
  - §4 calibration chips (app only, E1.1)
  - p29 summary (A)
- **Super admin:** Users / New User / Create User (E1.17/19/21), company required for Company Admins (E1.20), §5.3 default recommendations (E1.22), §5.4 Dairy Processing Companies (E1.23), OEM Specs (G), Regions no longer in the menu (C1).
- **Company admin:** farm review banner (C3), Dairy Processing Companies.

### J. Data checks (no code)

1. **Phone numbers.** Testers per phone number on staging, read-only. If most testers in a company share one number, it's the company line.
   ```sql
   SELECT u.PhoneNumber, COUNT(*) AS Testers, MIN(c.Name) AS Company, COUNT(DISTINCT u.TestingCompanyId) AS Companies
   FROM AspNetUsers u LEFT JOIN TestingCompanies c ON c.Id = u.TestingCompanyId
   GROUP BY u.PhoneNumber ORDER BY Testers DESC;
   ```
   Also count testers with no phone. Only the migration sets `PhoneNumber`, from legacy `MobileNo` then `PhoneNo` (`tools/Migration/Pipeline/MigrationRunner.cs:192,243,292`). No admin page edits it, so new users print no phone.
2. **Default recommendations.**
   - 123 of the 186 seeded fault observations have no default recommendation (`Data/SeedData/faultObservations.json`), so the tester types one.
   - Send Maria the list (CSV) to fill in, or she edits them on the Fault Observations admin page.

---

## Questions for Maria (for tomorrow)

1. Licence email: the 60-day and 30-day text, and who receives it (tester only, or also their company admins).
2. Nitro Pro: version, the exact error, and which file failed. Does "universal" mean archival PDF/A?
3. Ratio spread: front vs back ±5%, or pooled highest − lowest (Jono's view)?
4. Cleaning reserve: which vacuum is the "cleaning vacuum"? (Not π. See D5.)
5. ISO 16a / 16b: the pass limits.
6. Test ports in place: which section, shown only with ISO ports, and the rating for "No".
7. Regulator not correct / not big enough: the rating and recommendation for the summary page.
8. Short test without ISO ports: what should be skipped?
9. Main airline: diameter in mm and length in m, correct?
10. Re-accepting terms on licence renewal: OK to drop?
11. Default recommendations for the 123 observations without one.
12. testing@ mailbox: their Microsoft 365 admin sets it up (not RocketSpark); we'll send the steps.
13. Old-test cutoff date for the "AutoRep Plus" banner.
14. Farm review stays with company admins (Josh's call), with a reminder banner. Tell Maria why.

## Suggested order

1. ~~Merge #88.~~ Done 19 Oct.
2. In parallel: **A** (report page one), **B** (licence access), **C1–C4** (admin), **G** (OEM Specs).
3. **D** once Maria answers D2, D4, D5, D8 and D9. Ship the unblocked parts (D1, D3, D6, D7) first.
4. **E**, then **F**, both after A, because all three edit the report file.
5. **H** once the text, recipients, mailbox and scope are settled.
6. **I** (guides) last; then regenerate the sample PDFs and the project update before sending anything to Maria.
