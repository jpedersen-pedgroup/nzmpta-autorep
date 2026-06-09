# NZMPTA AutoRep — Build Checklist (all phases)

> Tracks the contracted **$43,750** scope: mandatory **M1–M6** + optional **O1, O2, O3**. *(O3 added by variation 8 Jun 2026, +$2,500.)*
> Status legend: ✅ Complete · 🟡 Partial · ⬜ Not started · ❓ Needs confirmation · ⛔ Out of contracted scope
> Organised by the **4 contract delivery phases** → **scope items (M/O)**. The repo's tracer-bullet build order lives in `plans/autorep-rebuild.md` (11 implementation phases); the mapping is noted per item.
>
> **Last assessed:** 5 Jun 2026 (against repo HEAD `fe83d8e`). **Indicative overall completion: ~20–25% by contract value.**

## Status summary

| Milestone | Scope item | Payment milestone | Status | Est. % |
|---|---|---|---|---|
| **M1** | Foundation, data model & shared platform | Phase 1 | 🟡 Substantial | ~70% |
| **M2** | Tester PWA core (offline + sync) | Phase 2 | 🟡 Early | ~20% |
| **M3** | Wizard test capture (steps 1–11) | Phase 2 | ⬜ Not started | ~5% |
| **M4** | Existing reports — PDF generation (7) | Phase 3 | ⬜ Not started | 0% |
| **M5** | Admin portal — users, companies & reference data | Phase 3 | 🟡 Identity done, ref-data not | ~50% |
| **M6** | Hardening, UAT, security review & go-live | Phase 4 | ⬜ Not started | 0% |
| **O1** | Data migration tooling & cutover | Phase 4 | ⬜ Not started | 0% |
| **O2** | Admin test view & edit + audit | Phase 3 | 🟡 View only | ~15% |
| **O3** | Pulsation-data PDF upload & merge into Final Report | Phase 3 | ⬜ Not started | 0% |

---

## Phase 1 — Foundation  ·  M1  ·  🟡 ~70%
*Repo plan: Phase 1 (walking skeleton) + Phase 8 (identity) merged in.*

### M1 — Foundation, data model & shared platform
- [x] ✅ Azure infrastructure as code — App Service, Azure SQL + Private Endpoint, Key Vault, Storage, App Insights, TLS 1.2+ (`infra/` Bicep, complete)
- [x] ✅ Azure infrastructure **deployed** (confirmed in place). Full hardening/security review deferred to M6. **Fixed 5 Jun:** prod region was `australiaeast` → corrected to `newzealandnorth` for NZ data-residency (staging already NZ North; SQL + Storage backups are LRS/in-region). *If prod was already deployed in AU, redeploy to NZ North before any data migration.*
- [x] ✅ CI/CD pipeline — build + unit tests on PR, deploy to staging on merge (`.github/workflows/app.yml`)
- [x] ✅ ASP.NET Core Identity — email + password, lockout (`Program.cs`, `Pages/Account/*`)
- [x] ✅ JWT access token (1h) + refresh token (7d sliding) with rotation & replay detection (`Api/AuthController.cs`, `Services/JwtTokenService.cs`, `Services/RefreshTokenService.cs`)
- [x] ✅ Tester licence-expiry check at login **and** refresh (`Pages/Account/Login.cshtml.cs`)
- [x] ✅ Audit-trail infrastructure — EF `SaveChangesInterceptor`, before/after JSON, same transaction (`Data/AuditInterceptor.cs`, `Domain/Entities/AuditEntry.cs`)
- [x] ✅ Base Razor Pages shell + role-gating (`/app` Tester, `/admin` Admin, cross-role 403) (`Program.cs`, `Domain/Roles.cs`)
- [ ] 🟡 **Full** EF Core domain model — only core entities exist (Tester, TestingCompany, Farm, MachineTest, AuditEntry, RefreshToken). Missing the wider schema: TestVersion, VendorSpecification(+Snapshot), EquipmentModel, TestStandardManual, Fault, ChecklistAttestation, FinalReportBlob, StandardRecommendationWording, SyncConflict
- [x] ✅ EF Core migrations (`Migrations/InitialCreate`, `Migrations/Phase8_*`)
- [ ] 🟡 Seed data — **dev-only** (`admin@local`, `tester@local`); no production/go-live seed
- [ ] ⬜ Data-migration tooling **skeleton** against staging (plan listed under Phase 1; see O1)
- [ ] ❌ Unit + integration tests for the above (M1 price includes testing — currently only an empty placeholder)

---

## Phase 2 — Tester core  ·  M2 + M3  ·  🟡 ~12%
*Repo plan: Phases 2–4.*

### M2 — Tester PWA core (offline + sync)
- [x] ✅ PWA installable — web manifest + service worker registered (`wwwroot/manifest.webmanifest`, `sw.js`, `js/pwa-register.js`)
- [x] ✅ Service worker caches **app shell** for offline UI (`sw.js`)
- [x] ✅ Sync endpoints `POST /api/sync/tests` (upsert by ClientId, idempotent, **now carries Machine Configuration**) + `GET /api/sync/tests` (pull) (`Api/SyncController.cs`) — 9 Jun
- [x] 🟡 IndexedDB local storage — **tests done** (`Client/db/testStore.ts`, `LocalTest` store); reference data still to come
- [ ] 🟡 Reference-data **logos offline** — service worker now runtime-caches `/api/milk-companies/{id}/logo` (viewed logos available offline). M2 still needs: **pre-cache all active logos on sync** (so unseen ones work too) + render tester pages offline so cached logos actually display
- [x] ✅ Offline-first **test creation** on-device (9 Jun) — the single Preact wizard creates/edits tests in IndexedDB; the create flow hands it the chosen farm
- [ ] 🟡 `syncState` — `local-only`/`uploaded` wired (badges in My Tests); `uploading`/`merge-conflict` await the Sync Reconciliation Engine
- [ ] ⬜ Vendor-specification caching for offline pass/fail
- [ ] ⬜ Reference-data delta sync endpoint (`GET /api/sync/reference-data?asOf=`)
- [ ] ⬜ Final Report blob sync endpoint (`/api/sync/final-report/{testId}`)
- [x] 🟡 **Tester test-history sync** — push local-only tests + **pull** the Tester's tests into IndexedDB (`Client/sync/syncClient.ts`, "Sync now" button). Still to come: delta/`asOf` cursor + background/login sync
- [ ] ⬜ **Offline reprint of historical tests** — regenerate the Final Report client-side from synced data + the test's standards snapshot (avoid bulk-storing PDFs on-device, esp. iOS); cache only recently-opened PDFs
- [x] 🟡 Offline/sync status indicators — per-test sync badge + "Sync now" in My Tests; full offline/online connectivity banner still to come
- [x] 🟡 Tests — **sync round-trip integration green** (`SyncControllerTests`) + Vitest (store/resolver/checklist); offline lifecycle still manual UAT

### M3 — Wizard test capture (steps 1–11)
- [ ] 🟡 Test setup step (select/create farm) — basic `Pages/App/Tests/New` exists; not the full wizard
- [ ] 🟡 Machine configuration step — **`MachineConfiguration` entity + EF migration done (8 Jun)** (plant type, cluster count, pulsator, claw/shell/liner, milkline, VSD, pump lubrication, ancillary flags, ISO ports, pulsator-stop); **UI step pending**
- [ ] ⬜ Ancillary equipment step (+ "Other" on every lookup)
- [x] ✅ Wizard Step Resolver — **.NET + TS mirror done (8 Jun)**: legacy-aligned steps + branch rules (rotary/herringbone, VSD min-speed, ACR/ancillary sections, optional cluster step, short-test flag); shared JSON fixtures in `tests/fixtures/wizard` drive **both** (6 xUnit + 6 Vitest green). TS toolchain stood up: npm + esbuild + Vitest under `src/Autorep.Web` (`Client/`), CI runs typecheck + Vitest. *(esbuild pinned ≥0.25 via override; remaining npm-audit items are dev-server-only vite advisories — prod deps 0 vulns.)*
- [ ] ⬜ Visual faults — pre-start (checklist + "Check all as verified" + attestation)
- [ ] ⬜ Visual faults — running (+ Guards-Installed-on-Pulsators boolean)
- [ ] ⬜ Vacuum & reserve tests (sections 1–7) with live pass/fail
- [ ] ⬜ Additional tests (sections 8–16), auto-hidden when equipment absent
- [ ] ⬜ Pulsator tests (per-pulsator rates/ratios; "faulty rows only" / "enter all")
- [ ] ⬜ Individual cluster tests (optional / skippable)
- [ ] ⬜ Pass/Fail Calculator (.NET + TS mirror, shared fixtures)
- [ ] ⬜ Fault Aggregator (.NET) + Fault Summary step with per-fault severity
- [ ] ⬜ Recommendations step (Standard Recommendation Wording dropdown + freeform)
- [ ] ⬜ Next Test Date (pre-populated +12 months) + compliance disclaimer
- [ ] ⬜ Review & sign-off step (+ back-navigation to any step)
- [ ] ❌ Wizard unit tests + Playwright happy-path

---

## Phase 3 — Reporting & admin  ·  M4 + M5 + O2 + O3  ·  🟡 ~30%
*Repo plan: Phases 5–10.*

### M4 — Existing reports (PDF generation)
- [x] ✅ **Engine decided (5 Jun 2026): client-side** (pdfmake + pdf-lib, on-device). NZMPTA requires testers to print on-site from portable printers and hand to farmers **while offline**, so server-side rendering (the proposal §4.1 QuestPDF line) cannot work. PRD already reflects client-side.
- [ ] ⬜ Test Summary (per-fault severity, Next Test Date, compliance disclaimer)
- [ ] ⬜ Test Report Results
- [ ] ⬜ Visual Faults Checklist
- [ ] ⬜ Test Record (incl. Vacuum Pump Speed/Capacity bug fixed)
- [ ] ⬜ Additional Testing
- [ ] ⬜ Individual Cluster Airflow Test
- [ ] ⬜ Pulsation System Result
- [ ] ⬜ Equipment-not-present sections hidden (not "N/A")
- [ ] ❌ Golden-file PDF tests (one per report)

### M5 — Admin portal — users, companies & reference data
- [x] ✅ Tester CRUD — list, create, edit, deactivate, reset password, force-logout (`Pages/Admin/Testers/*`)
- [x] ✅ Testing Company CRUD (`Pages/Admin/Companies/*`)
- [x] ✅ Role assignment + licence-expiry management (`Pages/Admin/Testers/Edit`)
- [x] ✅ Self-service password reset + forced-reset-on-first-login (`Pages/Account/ForgotPassword`, `ResetPassword`)
- [x] ✅ 2FA **enrolment** — TOTP setup, challenge, recovery codes (`Pages/Account/SetupAuthenticator`, `TwoFactorChallenge`, `RecoveryCodes`)
- [ ] 🟡 2FA **enforcement** (every 30 days / new device) — enrolment only, not enforced
- [ ] ⬜ Equipment Catalogue management (11 equipment types)
- [ ] ⬜ Vendor Specification editor (with Effective Date picker)
- [ ] ⬜ Standard Recommendation Wording CRUD
- [ ] ⬜ Test Standard Manual upload (versioned, retained, downloadable)
- [x] ✅ Region & Milk Supply Company catalogues — schema, seed **and admin CRUD built (5 Jun)** (list / create / edit / deactivate; Super-Admin only)
- [x] ✅ Farm Details edit — **built (5 Jun)**: `/Admin/Farms` Index + Edit; Super-Admin edits any, Company-Admin scoped to farms with completed tests by their testers.
- [x] ✅ NZ Post address autocomplete on Farm Edit — **built (5 Jun)**: keyless legacy suggest+details via server-side proxy (`/api/address/*`); fills Address/Town/PostCode. Online-only; manual entry always available.
- [ ] ⬜ Company-level reporting for Company Administrators
- [ ] ❌ Admin portal CRUD Playwright tests

### O2 — Admin test view & edit + audit
- [x] ✅ Test **list** view `/admin/tests` — Super-Admin sees all; Company-Admin scoped to their own company's tests (5 Jun 2026) (`Pages/Admin/Tests/Index`)
- [ ] 🟡 Test **detail** view (read-only; not the full wizard-style render)
- [ ] ⬜ Filter chips (Tester, Company, Farm, date range, status, has-conflicts)
- [ ] ⬜ Edit any field → new Test Version (Test Versioning Engine)
- [ ] ⬜ Audit panel — version timeline + per-field diffs + ChecklistAttestation events
- [ ] ⬜ Regenerate Final Report on admin edit (same engine as PWA)
- [ ] ⬜ Soft-delete with mandatory reason
- [ ] ❌ O2 Playwright path (edit synced test → report regenerates)

### O3 — Pulsation-data PDF upload & merge into Final Report *(added by variation 8 Jun 2026, +$2,500)*
- [ ] ⬜ Upload pulsation-graph PDF against a test (tester PWA + admin portal)
- [ ] ⬜ Store the uploaded blob + offline-capable upload queue (syncs when back online)
- [ ] ⬜ Merge the uploaded PDF into the generated Final Report (pdf-lib)
- [ ] ⬜ Final Report blob sync carries the merged output
- [ ] ❌ Tests (merge golden-file + upload round-trip)
*(Repo `PRD.md` + `plans/autorep-rebuild.md` Phase 6 already assume O3 — now correctly in contracted scope.)*

---

## Phase 4 — Hardening & cutover  ·  M6 + O1  ·  ⬜ 0%
*Repo plan: Phase 11 + cross-cutting hardening.*

### O1 — Data migration tooling & cutover
- [ ] ⬜ Standalone .NET console migration project (none exists yet)
- [ ] ❓ Legacy Azure SQL read access + sample tester accounts obtained (NZMPTA dependency — blocks design)
- [ ] ⬜ Legacy → new schema mapping (companies, testers, farms, tests, vendor specs, equipment)
- [ ] ⬜ Row-level quarantine + data-quality CSV report
- [ ] ⬜ Idempotent dry-run mode (staging) + single-shot cutover guard
- [ ] ⬜ Migrated testers flagged `forcedPasswordResetRequired`
- [ ] ⬜ **Migrated Machine Tests attributed to the migrated Tester** so each Tester can browse / download / **reprint their full history** (migrate as DATA; surfaced offline via the M2 test-history sync below)
- [ ] ⬜ Sample migrated tests regenerated & compared to legacy reports
- [ ] ❓ **Reprint strategy for migrated tests** — regenerate client-side from migrated data + historical standards snapshot (preferred; legacy DB has no PDF blob) vs. migrate stored legacy PDFs; and must a reprint match the legacy layout exactly? (NZMPTA decision — affects O1/M4)
- [ ] ❌ Golden-record migration test + cutover runbook

### M6 — Hardening, UAT support, security review & go-live
- [ ] ⬜ Performance testing (20–30 concurrent sessions)
- [ ] ⬜ Security review (TLS, private endpoint, secrets, auth, headers)
- [ ] ⬜ Full integration test pass across delivered scope
- [ ] ⬜ UAT cycles with NZMPTA (scripted scenarios; Maria Scott sign-off)
- [ ] ⬜ Tester onboarding communications + documentation
- [ ] ⬜ Parallel-run period
- [ ] ⬜ Production cutover + legacy decommission (Go-Live)

---

## Out of contracted scope (add only by written variation)
- [ ] ⛔ **O4** Proactive notification schedule (upcoming/overdue reminders) — $2,500
- [ ] ⛔ **O5** Vendor self-service portal (Phase 2 candidate) — $3,500

> **O3** (pulsation-data PDF upload & merge) was **added to scope by variation on 8 Jun 2026** (+$2,500 → $43,750) — see Phase 3. *Formalise the written variation + confirm final price.*

## Cross-cutting / carried through every phase
- [ ] 🟡 Automated test suite — **harness + first Playwright E2E (5 Jun)**: xUnit unit + `WebApplicationFactory` integration + a Playwright happy-path (admin farm edit + NZ Post autocomplete), **11 green**; CI runs E2E as its own job. Still need golden-file PDF + wizard/broader coverage. See `plans/test-schedule.md`
- [x] ✅ Audit logging of admin actions (interceptor in place; 7-yr retention is an Azure backup/config item)
- [ ] ⬜ Brand assets applied (logo, colours, typography) — NZMPTA dependency, before Phase 3
- [ ] ⬜ Accessibility baseline (semantic HTML, ARIA, keyboard, contrast)

## Notes & open items
- **❓ Open item for NZMPTA (farms):** Are Farms created/loaded by NZMPTA or admins **before** a tester visits, or do testers create them **on-farm**? This drives the farm-picker UX and whether **offline** farm creation (M2) is essential. Current tester new-test flow: pick an existing farm, or add one via an in-page modal (online only for now).
- **Biggest risks to "done":** (1) grow the partial domain model to the full schema; (2) **establish the test suite** — it's near-zero today and M1/M4/etc. prices include testing. *(Infra confirmed deployed; prod region corrected — see M1.)*
- **Resolved (5 Jun 2026):** report engine is **client-side** (pdfmake/pdf-lib) for offline on-site printing; proposal §4.1 (QuestPDF/server-side) is superseded. No price change (offline print was always required) — worth noting to NZMPTA as a clarification, ideally in a Requirements v1.2.
- **Farm details schema built (5 Jun 2026):** `Farm` expanded (identity, location, farmer contact, `IsActive`, `UpdatedAt`) + `Region` and `MilkSupplyCompany` reference tables (nullable FKs, seeded 16 regions / 10 processors, cached offline). Migration `FarmDetailsAndReferenceData`; builds clean. **Edit authorization:** Company Administrator edits Farm Details for farms tied to completed tests by *their* testers (scoped); Super-Administrator edits any — both are online admin screens, so **NZ Post address autocomplete is viable there** (online-only; still a paid integration outside the $41,250 → variation). Tester on-farm farm creation stays manual (offline). **Open design Q (still open):** the edit UI implements the **propagate** model — editing the shared Farm updates it for all that farm's tests, with `UpdatedAt` + the audit interceptor recording the change. If NZMPTA needs per-test snapshots of farm details as-at test time, that's a future change.
- **Admin test list scoped to company (5 Jun 2026):** `/admin/tests` now filters by the viewer's Testing Company — a Company Administrator sees only Machine Tests performed by Testers in their own company; Super-Administrator sees all (`Pages/Admin/Tests/Index`, mirrors the `/Admin/Farms` in-page scoping). **Open design Q (still open):** scoping follows the Tester's *current* `TestingCompanyId` because `MachineTest` carries no company of its own — if a Tester moves companies, their historical tests move with them. If NZMPTA needs point-in-time company ownership (a Test attributed to the company as-at test time), denormalise a `CompanyId`/snapshot onto `MachineTest` — a future schema change, the direct analog of the Farm-details snapshot question above.
- **Offline history & reprint (8 Jun 2026, Josh):** Testers must be able to download & **reprint their historical (incl. migrated legacy) tests offline**. Proposed approach: delta-sync each Tester's tests as **DATA** into IndexedDB (not bulk PDFs); **reprint = regenerate client-side** from data + the standards snapshot. **Open Qs for NZMPTA:** (1) do legacy PDFs exist anywhere (legacy DB has no PDF blob → likely regenerate from data); (2) must a reprinted legacy test match the old layout exactly, or is new-format rendering acceptable? Affects O1/M4 scope.
- **NZMPTA dependencies blocking work:** legacy SQL access (O1), brand assets (M4/M5), wizard validation workshop (M3), §14 confirmations.
