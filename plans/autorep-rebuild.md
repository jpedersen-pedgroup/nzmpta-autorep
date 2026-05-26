# Plan: NZMPTA AutoRep Rebuild — Committed Scope (M2–M5 + O1–O3)

> Source PRD: `PRD.md` (this repo) — derived from `docs/NZMPTA_AutoRep_Rebuild_Requirements_v1_1.docx`.
> Ubiquitous language: `UBIQUITOUS_LANGUAGE.md`.

## Architectural decisions

Durable decisions that apply across every phase. Phase work refers back here rather than re-stating them.

### Routes

- `/` — role-based redirect (Tester → `/app`, Admin → `/admin`).
- `/app/*` — Tester PWA scope (service worker controlled).
- `/admin/*` — Admin Portal (online-only Razor Pages).
- `/api/auth/*` — Identity endpoints (login, refresh, logout, password reset, 2FA challenge).
- `/api/sync/tests` — Machine Test upload + reconciliation.
- `/api/sync/reference-data` — reference-data delta endpoint (cursor-based).
- `/api/sync/final-report/{testId}` — Final Report PDF blob upload + download.

### Schema (key tables and relationships)

- **Tester** — auth identity, **TestingCompany** foreign key, **TesterLicence** one-to-one.
- **TesterLicence** — `licenceExpiryDate`, history of renewals.
- **TestingCompany** — name, contacts, `isActive`.
- **Farm** — first-class entity; `MachineTest` references `Farm` by id at creation.
- **MachineTest** — `testerId`, `farmId`, `machineConfiguration`, `testStartedAt`, `markedCompleteAt`, `testStandardManualVersionId`, `nextTestDate`, soft-delete columns, `currentVersionNumber`.
- **TestVersion** — immutable historical snapshot of a `MachineTest` per edit; carries actor + timestamp + per-field before/after.
- **VendorSpecificationSnapshot** — child of `MachineTest`, one per Equipment Model tested; stores `vendorSpecificationId + effectiveDate + snapshottedAt`.
- **VendorSpecification** — per Equipment Model, **EffectiveDate** column; latest row with `effectiveDate <= now` is "currently effective".
- **EquipmentModel** — per equipment type (VacuumPump, Pulsator, Liner, Shell, Claw, Jetter, ACR, MilkMeter, MilkFlowIndicator, Regulator, ReleaserPump); supports deprecation.
- **TestStandardManual** — versioned PDF blobs; every version retained.
- **StandardRecommendationWording** — curated dropdown options.
- **Fault** — child of `MachineTest`, carries `severity` (Critical / Major / Minor) + provenance (source section).
- **ChecklistAttestation** — child of `MachineTest`, records each use of "Check all as verified" (`wizardStepId + checkedAt + actor + attestationText`).
- **FinalReportBlob** — latest client-uploaded Final Report PDF per `MachineTest`.
- **AuditEntry** — actor + timestamp + entity + per-field before/after; 7-year retention.
- **SyncConflict** — flagged overlapping-field merges from the Sync Reconciliation Engine.

### Key models (deep modules)

- **.NET** — Pass/Fail Calculator, Wizard Step Resolver, Fault Aggregator, Sync Reconciliation Engine, Test Versioning Engine, Reference Data Snapshot Service, Audit Recorder (EF Core `SaveChangesInterceptor`), Migration Tool (standalone console).
- **TypeScript (runs in both Tester PWA and Admin Portal browser)** — Report Generator (pdfmake), Final Report Composer (pdf-lib), plus mirrors of Pass/Fail Calculator and Wizard Step Resolver. Mirrored modules share a fixture-set; both implementations must agree.

### Authentication & authorization

- ASP.NET Core Identity, email + password.
- Roles: `Tester`, `CompanyAdministrator`, `SuperAdministrator`.
- JWT Access Tokens — 1h Tester, 2h Admin.
- Sliding Refresh Tokens — 7d Tester, 1d Admin; rotated on use; invalidated on password change / logout / deactivate / force-logout.
- Tester Licence enforced at login pipeline (not per request) so an expiry mid-Test doesn't break offline work.
- Super-Administrator 2FA — TOTP with email-code fallback; required every 30 days and on every new/unrecognised Device.
- Self-service password reset via email link with 1h TTL.
- Migrated Tester accounts: `forcedPasswordResetRequired = true` flag set at migration time; consumed on first login.

### Sync protocol

- Per-Test `syncState` in IndexedDB: `local-only` → `uploading` → `uploaded` (or `merge-conflict`).
- Upload payload: full Test state + last-known `serverVersionId` + the Final Report PDF blob.
- Server delegates the merge decision to the **Sync Reconciliation Engine** (pure function).
- Reference-data fetch: client sends `referenceDataAsOf` cursor; server returns delta (new/updated Vendor Specifications with `effectiveDate <= now`, current Test Standard Manual version if newer).
- Field-level merge: Tester-owned fields and admin-owned fields live in disjoint groups. Non-overlapping edits merge cleanly. Overlapping edits resolve last-writer-wins at field granularity; both states preserved as TestVersion rows + SyncConflict row.

### Report generation locus

- **Client-side**, in the browser, in both the Tester PWA and the Admin Portal.
- Generation: **pdfmake**. Merging Pulsation Data PDF: **pdf-lib**. Both MIT-licensed; ~600 KB combined; verified working on iPad Safari and Android Chrome.
- Fonts bundled into the PWA so generation is deterministic offline.
- Server stores the most-recent client-uploaded Final Report blob; **server never generates Reports**.
- Email of Reports is not a platform feature — Testers use their own email client from the downloaded PDF.

### Hosting & cross-cutting

- Azure NZ North (App Service + Azure SQL with Private Endpoint + Key Vault + Storage + Application Insights).
- TLS 1.2+ end-to-end. No credentials in client code.
- 35-day backups; data residency confined to NZ.
- CI/CD: one pipeline, automated tests gate every PR.

---

## Phase 1: Walking skeleton — end-to-end tracer

**User stories**: #1, #2, #10 (partial), #31, #32 (partial), #47, #65, #71, #80, #83

### What to build

The thinnest possible end-to-end slice that exercises every architectural layer. A Tester logs in to the PWA on their iPad, creates a Machine Test that captures only the Farm Name + a Tester Name field, marks it complete, and on next sync the Test appears in the Admin Portal Tests list. Super-Administrator can log in to the Admin Portal and see the same Test. No Wizard logic, no Reports, no Reference Data yet — just the plumbing.

The point of this phase is to prove the architecture works end-to-end before any feature work begins. Every layer (Azure deploy, .NET app, EF Core, Identity, role-gating, PWA service worker, IndexedDB, sync API, Admin Portal) must be exercised by a real round-trip.

### Acceptance criteria

- [ ] Azure environment provisioned (App Service, Azure SQL with Private Endpoint, Key Vault, Storage, Application Insights). TLS 1.2+ enforced.
- [ ] CI/CD pipeline runs on every PR: build, unit tests, deploy to staging on merge to main.
- [ ] A Tester account and a Super-Administrator account can be created via seed migration.
- [ ] Login at `/api/auth/login` returns Access Token + Refresh Token; Refresh Token rotation works.
- [ ] `/app` is reachable only by Tester role; `/admin` only by Super-Administrator role; cross-role access returns 403.
- [ ] PWA installs on iPad Safari and desktop Chrome (manifest + service worker registered).
- [ ] Service worker serves the PWA shell offline (kill connectivity, app loads from cache).
- [ ] Tester creates a Machine Test from `/app` capturing Farm Name and Tester Name; Test is stored in IndexedDB with `syncState = local-only`.
- [ ] On connectivity restore, Test uploads via `POST /api/sync/tests` and `syncState` becomes `uploaded`.
- [ ] Super-Administrator visits `/admin/tests` and sees the synced Test in the list.
- [ ] EF Core migration scaffold + initial schema (Tester, TestingCompany, MachineTest, Farm stub) applied.
- [ ] Audit Recorder writes an AuditEntry row inside the same transaction as the MachineTest insert (verified by integration test).
- [ ] One Playwright smoke test: login as Tester, create Test, mark complete; login as Super-Admin, assert Test visible.

---

## Phase 2: Machine Configuration + Wizard Step Resolver + first Visual Faults step + "Check all as verified"

**User stories**: #12, #13, #14, #15 (Pre-Start only), #16, #17, #18, #29

### What to build

The Wizard becomes configuration-driven. The Tester declares the Machine Configuration upfront (machine type, pump set, pulsator, claw, shell, liner, milkline size, herd size, last BMCC); the **Wizard Step Resolver** then decides which subsequent Wizard Steps are visible. The first concrete Wizard Step beyond Configuration is **Visual Faults — Pre-Start**, presented as a fixed checklist of items, with a "Check all as verified" control at the bottom that requires the Tester to confirm an attestation prompt before bulk-ticking.

Equipment dropdowns include an "Other" option (no Vendor Specification implied). Tester can delete in-progress Tests from their list.

End-to-end: configuration + checklist state + ChecklistAttestation events sync to the server and are visible in the Admin Portal Test detail view (basic read-only render is fine).

### Acceptance criteria

- [ ] Setup Wizard step captures the full Machine Configuration; required fields validated client-side.
- [ ] **Wizard Step Resolver** (.NET + TypeScript mirror, shared fixture-set) returns the ordered list of visible Wizard Steps for a given Machine Configuration. Both implementations agree on every fixture.
- [ ] Visual Faults — Pre-Start step renders the fixed checklist; individual items can be ticked.
- [ ] "Check all as verified" control at the bottom of the step opens a modal with the attestation text; only on confirmation does the bulk-tick apply.
- [ ] A ChecklistAttestation row is written per use of "Check all as verified" (actor, timestamp, step id, attestation text).
- [ ] Any Equipment dropdown accepts an "Other" selection without a Vendor Specification.
- [ ] Tester deletes an in-progress (not yet Mark-as-Complete) Machine Test from their list; it disappears for that Tester.
- [ ] Synced Test in the Admin Portal shows the configuration, checklist state, and ChecklistAttestation events (read-only).
- [ ] Unit tests: Wizard Step Resolver across minimum-config, fully-loaded, and pathological-combination fixtures.
- [ ] Playwright: Tester completes Configuration → Visual Faults — Pre-Start (using "Check all as verified") → Mark-as-Complete → Test appears in Admin Portal with ChecklistAttestation recorded.

---

## Phase 3: Vendor Specifications + Pass/Fail Calculator + Vacuum & Reserve readings

**User stories**: #19 (Sections 1–7), #20, #21, #35, #36

### What to build

The first numerical-input Wizard Steps land along with the **Pass/Fail Calculator**. Vendor Specifications are seeded into the database via migration (no admin UI yet — that's Phase 9), each carrying an **EffectiveDate**. When a Tester starts a new Machine Test, the **Reference Data Snapshot Service** assembles the bundle of currently-effective Vendor Specifications and persists a **VendorSpecificationSnapshot** per Equipment Model tested. The Tester enters numerical readings for Sections 1–7 (Vacuum & Reserve tests) and the Wizard shows live pass/fail indicators next to each reading.

The reference-data sync delta endpoint goes live; the PWA fetches new/updated Vendor Specifications via the cursor-based protocol and stores them in IndexedDB.

### Acceptance criteria

- [ ] **VendorSpecification** table + EFCore migration; seeded with a representative dataset (real data slot once §14 access is granted).
- [ ] **Reference Data Snapshot Service** (.NET) returns the bundle of Vendor Specifications effective as of a given date. Unit tests cover past-effective, future-effective (excluded), today-effective, multiple-rows-pick-latest.
- [ ] `GET /api/sync/reference-data?asOf={cursor}` returns the delta of new/updated Vendor Specifications + current Test Standard Manual version.
- [ ] PWA on sync fetches the delta and updates IndexedDB.
- [ ] When a Machine Test is started, a VendorSpecificationSnapshot is written per Equipment Model tested, capturing `vendorSpecificationId + effectiveDate + snapshottedAt`.
- [ ] **Pass/Fail Calculator** (.NET and TypeScript mirror, shared fixture-set): given a measurement + a Vendor Specification, returns Pass / Fail / NoStandardAvailable.
- [ ] Vacuum & Reserve test Sections 1–7 render in the Wizard; numerical input with units; per-pulsator rates support "only faulty rows" and "enter all" modes.
- [ ] Live pass/fail indicators appear next to numerical entries, driven by the TypeScript Pass/Fail Calculator running against the snapshotted Vendor Specifications.
- [ ] Synced Test in the Admin Portal shows the numerical readings + pass/fail verdicts (read-only).
- [ ] Unit tests on both Pass/Fail Calculator implementations agree on the full fixture set.

---

## Phase 4: Visual Faults Running + Additional Tests + Fault Aggregator + Recommendations + Sign-off

**User stories**: #15 (Running), #16, #22, #23, #24, #25, #26, #27, #28

### What to build

The Wizard becomes feature-complete (excluding Reports). The Tester walks through Visual Faults — Running Inspection (including the Guards Installed on Pulsators boolean), Additional Tests Sections 8–16 with live pass/fail, an optionally-skippable Individual Cluster Tests step, the Fault Summary step (auto-populated by the **Fault Aggregator**), the Recommendations step (Standard Recommendation Wording seeded via migration; admin UI in Phase 9), the Next Test Date step (pre-populated 12 months out), and the Review & Sign-Off step. The Tester can navigate back to any prior step before Mark-as-Complete.

### Acceptance criteria

- [ ] Visual Faults — Running Inspection step renders the running-stage checklist + Guards Installed on Pulsators boolean. "Check all as verified" available.
- [ ] Additional Tests Sections 8–16 render numerical input with live pass/fail.
- [ ] Individual Cluster Tests step is skippable via a "Skip this step" control; skip is recorded on the Test.
- [ ] Each Fault carries severity (Critical / Major / Minor) and a description.
- [ ] **Fault Aggregator** (.NET, unit-tested): collects Faults from Visual Faults + numerical-test issues, groups by section, returns a structured FaultSummary.
- [ ] Fault Summary step renders the auto-populated FaultSummary; the Tester can edit Fault descriptions and severities but not add/remove auto-collected Faults.
- [ ] StandardRecommendationWording seeded; Recommendations step renders the curated dropdown with the option to type freeform text.
- [ ] Next Test Date step pre-populates to today + 12 months; editable.
- [ ] Review & Sign-Off step renders a full summary of the Test for verification.
- [ ] Tester can use a Back control at any step before Mark-as-Complete; data on later steps is preserved if still valid.
- [ ] Synced Test in the Admin Portal shows the full Test state (read-only) including FaultSummary, Recommendations, Next Test Date.
- [ ] Playwright smoke covers the full Wizard happy path through Sign-Off (no Mark-as-Complete yet — Reports land in Phase 5).

---

## Phase 5: Client-side Report Generator — first named Report (Test Summary) + Final Report blob sync

**User stories**: #31, #37, #38 (partial), #41, #42, #43, #46

### What to build

The first named Report — **Test Summary** — is generated client-side in the PWA using pdfmake. The Final Report Composer (pdf-lib) wraps it as a one-Report Final Report (Pulsation merge lands in Phase 6). On Mark-as-Complete, the PWA generates the Final Report locally, the Tester can download or print it offline, and a copy is retained on the Device. On the next sync, the PDF blob is uploaded to the server and stored in FinalReportBlob; the Admin Portal can display the uploaded PDF.

A golden-file PDF test pins the Test Summary's pixel-equivalent contract.

### Acceptance criteria

- [ ] pdfmake + pdf-lib + bundled fonts shipped with the PWA; total bundle size impact measured and documented.
- [ ] **Report Generator** (TypeScript) renders the Test Summary template from a MachineTest + ReferenceDataSnapshot.
- [ ] Test Summary includes: per-Fault severity (replacing legacy group-level), Next Test Date, Compliance Disclaimer.
- [ ] **Final Report Composer** (TypeScript) returns the generated PDF as-is when no Pulsation Data PDF is attached.
- [ ] Mark-as-Complete enables the Download / Print buttons on the Sign-Off step; both work with no connectivity.
- [ ] Final Report PDF is retained in IndexedDB for the completed Test.
- [ ] `POST /api/sync/final-report/{testId}` accepts the PDF blob; server stores in **FinalReportBlob**.
- [ ] Admin Portal Test detail view has a "Download Final Report" link that returns the most-recent uploaded PDF.
- [ ] Golden-file PDF test (Vitest/Node.js): given a known Machine Test fixture, the generated Test Summary PDF matches a checked-in expected PDF after normalisation (strip generation timestamps, fonts embedded).
- [ ] Playwright extension: the existing full-Wizard smoke now Mark-as-Completes and downloads a non-empty Test Summary PDF.

---

## Phase 6: Remaining 6 named Reports + Pulsation Data PDF upload + merge

**User stories**: #38 (full), #39, #40, #44, #45

### What to build

All 7 named Reports are now generated client-side: Test Summary (from Phase 5), Test Report Results, Visual Faults Checklist, Test Record, Additional Testing, Individual Cluster Airflow Test, Pulsation System Result. Each Report omits sections corresponding to equipment not present on the Machine (not shown as N/A). The legacy Vacuum Pump Speed / Capacity population bug is fixed and verified by golden-file.

A Pulsation Data PDF drop-zone appears on the summary Wizard Step; the Final Report Composer appends the uploaded PDF as the last pages of the Final Report, on-device, at Mark-as-Complete.

### Acceptance criteria

- [ ] Each of the remaining 6 named Reports has a pdfmake template in the Report Generator.
- [ ] Equipment-not-present sections are absent from generated Reports (verified by golden-file comparison against a minimum-config Machine fixture).
- [ ] Vacuum Pump Speed and Capacity fields populate correctly on Test Record (golden-file regression test specifically targets this).
- [ ] Pulsation Data PDF drop-zone accepts PDFs up to a configured size limit; rejects non-PDFs with a clear error.
- [ ] Uploaded Pulsation Data PDF is stored in IndexedDB on the Test and uploaded to Azure Storage on next sync (separate from the Final Report PDF).
- [ ] **Final Report Composer** merges the generated Reports + the Pulsation Data PDF, with Pulsation pages appearing at the tail of the Final Report. Fixture test asserts page count and tail-pages identity.
- [ ] Golden-file PDF test exists for each named Report.
- [ ] Playwright happy-path now uploads a fixture Pulsation Data PDF and asserts the downloaded Final Report's page count includes the Pulsation pages.

---

## Phase 7: Test Versioning + Tester edit-after-complete + audit panel

**User stories**: #30, #50, #67, #68, #69, #72

### What to build

The **Test Versioning Engine** lands. A Tester can edit a completed Machine Test from their list; the edit produces a new **TestVersion** row, preserving the original state immutably. Every edit also produces AuditEntry rows with per-field before/after. The Admin Portal grows an audit panel on the Test detail view showing the full version timeline with diffs and ChecklistAttestation events; the panel surfaces whether each item on each checklist was individually verified or bulk-confirmed via "Check all as verified".

### Acceptance criteria

- [ ] **Test Versioning Engine** (.NET, unit-tested): given an existing MachineTest + an edit + an actor, produces a new TestVersion row capturing prior state, advances `currentVersionNumber`, and writes AuditEntry rows.
- [ ] Tester can open a completed Test from their list and re-enter Wizard-edit mode; saving produces a new TestVersion.
- [ ] Prior TestVersion rows are immutable (verified by an integration test that attempts a direct update and confirms it fails).
- [ ] AuditEntry captures actor + timestamp + per-field before/after JSON for every edit.
- [ ] Admin Portal Test detail view has an audit panel: version timeline with per-version actor + timestamp + diff against prior version; ChecklistAttestation events shown inline.
- [ ] Audit panel renders each checklist item with a marker indicating "individually verified" vs "bulk-confirmed via Check all as verified" (driven by ChecklistAttestation rows).
- [ ] On Tester edit-after-complete, the Final Report is regenerated client-side and the new PDF blob uploaded on sync (replacing the prior FinalReportBlob).
- [ ] Audit Recorder integration test confirms that a failed entity write produces no AuditEntry.

---

## Phase 8: Identity management — Tester / Company / Licence + 2FA + Refresh Token lifecycle

**User stories**: #3, #4, #5, #6, #7, #8, #9, #51, #53, #54, #55, #56, #57, #58, #80

### What to build

The Admin Portal grows the full identity-management surface. Super-Administrator can create/deactivate Testers, reset their passwords, force-logout, set/renew the Tester Licence Expiry Date, and assign Testers to Testing Companies. Tester Licence enforcement at login goes live with the 6-month and final-2-month every-login reminders. Two-factor authentication for Super-Administrator (TOTP + email-code fallback) is wired in, required every 30 days and on every new/unrecognised Device. Refresh Token lifecycle (rotation, sliding window, invalidation on force-logout) is hardened. Testing Company CRUD lands. The Company Administrator role can view their team's Tests (read-only here — edits land in Phase 9).

### Acceptance criteria

- [ ] Admin Portal Tester pages: list, create, edit, deactivate, reset password, force-logout, view active sessions.
- [ ] **TesterLicence** model + `licenceExpiryDate` column; Admin Portal page to set/renew.
- [ ] Login pipeline reads the current TesterLicence and refuses login if expired; offline tokens already issued continue to work until they naturally expire (so a mid-Test expiry doesn't break offline work).
- [ ] Tester sees a reminder banner on every login within 6 months of expiry, and a more prominent reminder within the final 2 months; reminder cannot be permanently dismissed until renewal.
- [ ] Force-logout: Super-Admin clicks "force logout" on a Tester; all Refresh Tokens for that Tester are invalidated; next API call from that Device returns 401.
- [ ] Self-service password reset: Tester enters email, receives link with 1h TTL; resetting invalidates all prior Refresh Tokens.
- [ ] Migrated Tester first-login: if `forcedPasswordResetRequired = true`, the Tester is redirected to a password change page before reaching `/app`. (Flag set by the Migration Tool in Phase 11.)
- [ ] Two-factor for Super-Administrator: TOTP setup flow + email-code fallback; challenge required every 30 days and on every new/unrecognised Device (Device identified by signed cookie).
- [ ] Refresh Token rotation: every refresh issues a new Refresh Token and invalidates the old one; replay of an old Refresh Token is rejected and triggers a force-logout of all sessions for that user.
- [ ] Testing Company CRUD: create, edit, deactivate; Testers can be assigned/reassigned.
- [ ] Company Administrator role + scoping: a Company Administrator sees only their team's Tests (read-only); attempts to read another company's data return 403.
- [ ] Login events written to AuditEntry (actor, timestamp, success/failure, IP).
- [ ] Playwright tests: Super-Admin creates a Tester → Tester logs in → mandatory password reset → completes a Test → Super-Admin force-logs-out → Tester's next sync fails with 401.

---

## Phase 9: Admin Reference Data + Company Admin edits + Sync Reconciliation hardening

**User stories**: #49, #50, #59, #60, #61, #62, #63

### What to build

NZMPTA gains full self-service over Reference Data. Super-Administrator manages Equipment Catalogues per equipment type, Vendor Specifications with an **EffectiveDate** picker (defaulting to today, with future dates supported), uploads new versions of the Test Standard Manual (every prior version retained and downloadable), and edits the Standard Recommendation Wording list. The PWA sync delta endpoint now correctly serves these admin-driven changes.

Company Administrators gain their first edit capability: Farm Details and the final summary / Recommendations on Tests by their team's Testers. These edits create TestVersion rows just like Tester edits.

Because admin edits can now race against Tester offline edits, the **Sync Reconciliation Engine** is hardened with full field-level merge logic + SyncConflict surfacing.

### Acceptance criteria

- [ ] Admin Portal pages for each Equipment Catalogue (VacuumPump, Pulsator, Liner, Shell, Claw, Jetter, ACR, MilkMeter, MilkFlowIndicator, Regulator, ReleaserPump): list, create, edit, deprecate. Deprecated models hidden from new-Test dropdowns but shown on historical Tests.
- [ ] Vendor Specification editor per Equipment Model with an Effective Date picker (defaults today, accepts future dates).
- [ ] Test Standard Manual upload page: upload new PDF, version auto-incremented; every prior version retained with download link.
- [ ] Standard Recommendation Wording CRUD page.
- [ ] Sync delta endpoint includes new/updated Vendor Specifications (effective as of sync time) and the current Test Standard Manual version.
- [ ] Company Administrator Test view gains "Edit Farm Details" and "Edit Final Summary / Recommendations" controls; saving creates a new TestVersion with the Company Admin as actor.
- [ ] **Sync Reconciliation Engine** unit tests pass for: Tester-only edit (no admin change), Admin-only Farm Details edit (no Tester change), non-overlapping concurrent edits (clean merge), overlapping concurrent edits (last-writer-wins per field, both states preserved as TestVersions, SyncConflict row written).
- [ ] Sync API integration tests round-trip the merge cases through the live API.
- [ ] Admin Portal audit panel surfaces SyncConflict rows for the affected Tests.
- [ ] PWA shows a non-blocking notice on Tests with unresolved SyncConflicts.

---

## Phase 10: O2 — Super-Admin Test browse + edit + soft delete + Admin-side Report regen

**User stories**: #64, #65, #66, #70, #73

### What to build

Super-Administrator gains the full Test browse-and-edit surface across all Testing Companies. A filterable list view (by Tester, Testing Company, Farm, date range, status, has-conflicts) lets them find any Test quickly. The detail view renders the Test in the same Wizard-style layout the Tester saw, with every field editable. Each save produces a new TestVersion (going via the Test Versioning Engine from Phase 7). On save, the Admin Portal regenerates the Final Report client-side using the same TypeScript Report Generator + Final Report Composer the Tester PWA uses, then re-merges the cached Pulsation Data PDF, and uploads the new PDF to FinalReportBlob.

Soft delete: Super-Administrator can mark a completed Test as deleted with a mandatory reason; the row + all TestVersions stay in the database for the 7-year audit window but are excluded from default views.

### Acceptance criteria

- [ ] Admin Portal Test list view with filter chips: Tester, Testing Company, Farm, date range, status, has-conflicts.
- [ ] Detail view renders the Test in a Wizard-style layout; every field is editable for Super-Administrator.
- [ ] Each save invokes the **Test Versioning Engine** → new TestVersion + AuditEntry rows.
- [ ] On save, the Admin Portal runs the TypeScript Report Generator + Final Report Composer in the browser and uploads the new Final Report PDF to FinalReportBlob.
- [ ] Soft delete: Super-Admin sees a "Delete Test" control; clicking opens a modal requiring a free-text reason; on confirm the Test is soft-deleted (rows retained but excluded from default queries).
- [ ] Default Test list views exclude soft-deleted Tests; a dedicated "Show deleted" filter surfaces them.
- [ ] Playwright: Super-Admin edits a synced Test → Final Report is regenerated → diff against the original Final Report shows the change.

---

## Phase 11: O1 — Migration Tool + cutover dry-runs

**User stories**: #4, #74, #75, #76, #77, #78, #79

### What to build

A standalone .NET console application reads the legacy Azure SQL database (read-only credential from Key Vault) and writes to the new schema. Each row in each legacy table is either mapped cleanly into the new schema or quarantined to a separate output table with a per-row error reason. After each run the tool produces a CSV data-quality report.

The tool runs in two modes: **dry-run** (writes to a staging database without affecting production, idempotent so it can run repeatedly) and **cutover** (writes to production, runs once at go-live). All migrated Tester accounts have `forcedPasswordResetRequired = true` set so legacy credentials cannot be used against the new platform.

Sample Tests migrated in dry-run are regenerated through the client-side Report Generator and compared against the original legacy Reports to validate the schema mapping and Vendor Specification snapshots.

### Acceptance criteria

- [ ] Standalone .NET console project; reads connection string + mode (`dry-run` | `cutover`) from configuration / Key Vault.
- [ ] Maps legacy tables → new schema for: Testing Companies, Tester accounts, Farms, Machine Tests (with historical Vendor Specification Snapshots), Vendor Specifications, Equipment Catalogue rows.
- [ ] Bad rows (validation failure, missing required fields, broken FKs) are quarantined to a separate output table with per-row error reasons — neither silently skipped nor halt-on-first.
- [ ] After each run, a CSV data-quality report lists row counts: migrated / quarantined per table.
- [ ] Dry-run mode is idempotent: re-running against an unchanged source produces an unchanged target + identical data-quality report.
- [ ] Cutover mode refuses to run if the target is not empty (single-shot guard).
- [ ] All migrated Tester accounts have `forcedPasswordResetRequired = true`.
- [ ] After a staging dry-run, a sample of migrated Tests are opened in the Admin Portal, Reports regenerated, and the output PDFs are compared (visually + page count) against the original legacy Reports for the same Tests.
- [ ] Golden-record migration test: a checked-in legacy database snapshot in, an expected new-schema state + expected data-quality CSV out. Test asserts the run produces both.
- [ ] Documentation: a runbook for the production cutover (pre-checks, run command, post-checks, rollback procedure).
