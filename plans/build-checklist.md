# NZMPTA AutoRep — Build Checklist (all phases)

> Tracks the contracted **$41,250** scope: mandatory **M1–M6** + optional **O1, O2**.
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
- [x] ✅ Sync upload endpoint `POST /api/sync/tests` (upsert by ClientId, idempotent) (`Api/SyncController.cs`)
- [ ] ⬜ IndexedDB local storage for tests / reference data
- [ ] ⬜ Offline-first **test creation** on-device (currently server-side Razor form only)
- [ ] ⬜ `syncState` state machine (`local-only` → `uploading` → `uploaded` / `merge-conflict`)
- [ ] ⬜ Vendor-specification caching for offline pass/fail
- [ ] ⬜ Reference-data delta sync endpoint (`GET /api/sync/reference-data?asOf=`)
- [ ] ⬜ Final Report blob sync endpoint (`/api/sync/final-report/{testId}`)
- [ ] ⬜ Offline/sync status indicators in the UI
- [ ] ❌ Tests (sync round-trip integration, offline lifecycle manual UAT)

### M3 — Wizard test capture (steps 1–11)
- [ ] 🟡 Test setup step (select/create farm) — basic `Pages/App/Tests/New` exists; not the full wizard
- [ ] ⬜ Machine configuration step (type, pumps, pulsator, claw, shell, liner, milkline, VSD, herd size, BMCC)
- [ ] ⬜ Ancillary equipment step (+ "Other" on every lookup)
- [ ] ⬜ Wizard Step Resolver (dynamic step visibility from configuration) — .NET + TS mirror, shared fixtures
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

## Phase 3 — Reporting & admin  ·  M4 + M5 + O2  ·  🟡 ~30%
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
- [ ] ⬜ Sample migrated tests regenerated & compared to legacy reports
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
- [ ] ⛔ **O3** Pulsator graph PDF upload & merge into Final Report — $2,500 *(note: repo PRD/plan Phase 6 still includes this)*
- [ ] ⛔ **O4** Proactive notification schedule (upcoming/overdue reminders) — $2,500
- [ ] ⛔ **O5** Vendor self-service portal (Phase 2 candidate) — $3,500

## Cross-cutting / carried through every phase
- [ ] ⬜ Automated test suite established (unit, integration, golden-file, Playwright) — *currently ~0%; see `plans/test-schedule.md`*
- [x] ✅ Audit logging of admin actions (interceptor in place; 7-yr retention is an Azure backup/config item)
- [ ] ⬜ Brand assets applied (logo, colours, typography) — NZMPTA dependency, before Phase 3
- [ ] ⬜ Accessibility baseline (semantic HTML, ARIA, keyboard, contrast)

## Notes & open items
- **Biggest risks to "done":** (1) grow the partial domain model to the full schema; (2) **establish the test suite** — it's near-zero today and M1/M4/etc. prices include testing. *(Infra confirmed deployed; prod region corrected — see M1.)*
- **Resolved (5 Jun 2026):** report engine is **client-side** (pdfmake/pdf-lib) for offline on-site printing; proposal §4.1 (QuestPDF/server-side) is superseded. No price change (offline print was always required) — worth noting to NZMPTA as a clarification, ideally in a Requirements v1.2.
- **Farm details schema built (5 Jun 2026):** `Farm` expanded (identity, location, farmer contact, `IsActive`, `UpdatedAt`) + `Region` and `MilkSupplyCompany` reference tables (nullable FKs, seeded 16 regions / 10 processors, cached offline). Migration `FarmDetailsAndReferenceData`; builds clean. **Edit authorization:** Company Administrator edits Farm Details for farms tied to completed tests by *their* testers (scoped); Super-Administrator edits any — both are online admin screens, so **NZ Post address autocomplete is viable there** (online-only; still a paid integration outside the $41,250 → variation). Tester on-farm farm creation stays manual (offline). **Open design Q (still open):** the edit UI implements the **propagate** model — editing the shared Farm updates it for all that farm's tests, with `UpdatedAt` + the audit interceptor recording the change. If NZMPTA needs per-test snapshots of farm details as-at test time, that's a future change.
- **Admin test list scoped to company (5 Jun 2026):** `/admin/tests` now filters by the viewer's Testing Company — a Company Administrator sees only Machine Tests performed by Testers in their own company; Super-Administrator sees all (`Pages/Admin/Tests/Index`, mirrors the `/Admin/Farms` in-page scoping). **Open design Q (still open):** scoping follows the Tester's *current* `TestingCompanyId` because `MachineTest` carries no company of its own — if a Tester moves companies, their historical tests move with them. If NZMPTA needs point-in-time company ownership (a Test attributed to the company as-at test time), denormalise a `CompanyId`/snapshot onto `MachineTest` — a future schema change, the direct analog of the Farm-details snapshot question above.
- **NZMPTA dependencies blocking work:** legacy SQL access (O1), brand assets (M4/M5), wizard validation workshop (M3), §14 confirmations.
