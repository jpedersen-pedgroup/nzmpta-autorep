# NZMPTA AutoRep — Build Checklist (all phases)

> Tracks the contracted **$43,750** scope: mandatory **M1–M6** + optional **O1, O2, O3**. *(O3 added by variation 8 Jun 2026, +$2,500.)*
> Status legend: ✅ Complete · 🟡 Partial · ⬜ Not started · ❓ Needs confirmation · ⛔ Out of contracted scope
> Organised by the **4 contract delivery phases** → **scope items (M/O)**. The original tracer-bullet build order lives in `plans/autorep-rebuild.md`; the GitHub phase issues (#2–#12) that mirrored it were closed on 7 Oct 2026 — **this file is the plan of record** for what remains.
>
> **Last assessed:** 7 Oct 2026 against `main` @ `91c673a` (PR #69). Every ✅ below was checked against the code on that commit; ❓ marks a claim that was not verified in code. **Indicative overall completion: ~75–80% by contract value.** The feature build is largely done; what remains is O2 admin editing, the offline shell, hardening/go-live, and the migration cutover itself.
>
> **Previous assessment:** 5 Jun 2026 @ `fe83d8e` (~20–25%).

## Status summary

| Milestone | Scope item | Payment milestone | Status | Est. % |
|---|---|---|---|---|
| **M1** | Foundation, data model & shared platform | Phase 1 | ✅ Done (prod never deployed — see M6) | ~95% |
| **M2** | Tester PWA core (offline + sync) | Phase 2 | 🟡 Capture + sync + offline print done; **offline navigation not** | ~65% |
| **M3** | Wizard test capture (steps 1–11) | Phase 2 | ✅ Done, with Sep-2026 tester-feedback leftovers | ~95% |
| **M4** | Existing reports — PDF generation (7) | Phase 3 | 🟡 Report built and split into selectable sections; Test Report Results not separately selectable; golden-file tests missing | ~90% |
| **M5** | Admin portal — users, companies & reference data | Phase 3 | 🟡 Company-admin test edits (PRD 49–50) and manual upload missing; 2FA enforced since PR #71 | ~85% |
| **M6** | Hardening, UAT, security review & go-live | Phase 4 | ⬜ Not started | ~5% |
| **O1** | Data migration tooling & cutover | Phase 4 | 🟡 Tool built and dry-run against staging; cutover not | ~60% |
| **O2** | Admin test view & edit + audit | Phase 3 | 🟡 **View only** — no edit, soft-delete, filters or audit panel | ~30% |
| **O3** | Pulsation-data PDF upload & merge into Final Report | Phase 3 | ✅ Done (tester side); no admin upload | ~85% |

---

## What is left to build — the short list

In priority order. Each line expands in the sections below.

1. **Production has never been deployed.** `app-prod.yml` and `infra-prod.yml` have zero runs. The workflow fixes landed 30 Sep (PR #69); the first infra + app deploy to `newzealandnorth` is the next step and unblocks everything in M6.
2. **O2 admin edit** — edit a synced test into a new version, soft-delete with a mandatory reason, list filter chips, audit panel. The admin viewer today is read-only (`Pages/Admin/Tests/View.cshtml` → wizard bundle in read-only mode).
3. **Offline tester shell (M2)** — `plans/offline-tester-app.md` Phases 2–4: navigation offline, cold launch offline, start-a-test offline, storage durability. Today every page navigation needs the server (`wwwroot/sw.js:281-282`).
4. **M6 hardening** — `plans/m6-infra-review.md` is untouched: security review, load test, alert rules, custom domain + certificate, test restore, WAF decision, remove the personal SQL admin, `CostCentre` tag, security headers/CSP (only HSTS is set today, `Program.cs:235`).
5. **O1 cutover** — NZMPTA review of the data-quality CSV, report-parity review of sample migrated tests, a real region check before cutover, cutover runbook, parallel run, legacy decommission. (`tools/Migration/README.md` now documents the tool as built.)
6. **Contracted items never started:** Company Administrator edits of the final summary / recommendations on their company's tests as a new version (PRD stories 49–50, rebuild plan Phase 9 — distinct from the Super-Administrator O2 surface), server-side Final Report store (`/api/sync/final-report`), Test Standard Manual upload, Vendor Specification Effective Date (❓ — see M5).
7. **Tester-feedback leftovers** (`plans/tester-feedback-2026-09-15.md`): oil-vs-water recommendation split by pump lubrication (4.1), ISO 16 final checks, and the open questions owned by Jono / Maria / NZMPTA.
8. **Tests still owed:** golden-file PDF (T4), wizard happy-path Playwright (T5), real-device iPad UAT incl. the 7-day storage rule (T6).

---

## Phase 1 — Foundation  ·  M1  ·  ✅ ~95%

### M1 — Foundation, data model & shared platform
- [x] ✅ Azure infrastructure as code — App Service, Azure SQL + Private Endpoint, Key Vault, Storage, App Insights, TLS 1.2+ (`infra/` Bicep)
- [x] ✅ **Staging** deployed in `newzealandnorth`; auto-deploys on every push to `main` (`.github/workflows/app.yml`, `/health` check)
- [ ] ⬜ **Production** deployed — **never run** as of 7 Oct 2026 (`infra-prod.yml`, `app-prod.yml` both manual, `prod` environment reviewers). Prod params already say `newzealandnorth`. Tracked under M6.
- [x] ✅ CI/CD — build, client typecheck + Vitest, xUnit unit/integration, Playwright E2E as its own job, TRX artefacts + job summary; prod deploy gated on E2E (PR #69); OIDC tokens only on the jobs that log in to Azure
- [x] ✅ ASP.NET Core Identity — email + password (12 chars, mixed case, digit), lockout 5 attempts / 15 min, forced reset on first login, self-service reset (Graph email in prod, logging sender otherwise)
- [x] ✅ JWT access + refresh tokens with rotation & replay detection (`Api/AuthController.cs`, `Services/RefreshTokenService.cs`)
- [x] ✅ Tester licence-expiry handling — lapsed licence gives a **sync-only session** rather than stranding unsynced work (`Domain/LicenceScope.cs`, `Services/TesterClaimsPrincipalFactory.cs`, PR #40)
- [x] ✅ Audit-trail infrastructure — `SaveChangesInterceptor`, before/after JSON, same transaction, PII redaction (`Data/AuditInterceptor.cs`, `AuditRedactionTests`)
- [x] ✅ Razor Pages shell + role-gating (`/app` Tester, `/admin` Admin, cross-role 403); `/api/*` returns 401/403 instead of redirecting (PR #37)
- [x] ✅ Domain model as built: Tester, TestingCompany (+logo), Farm (+Region, MilkSupplyCompany, review state), MachineTest (+Version/ReplacesClientId, NextTestDate, PayloadJson), MachineConfiguration, PumpDetails, EquipmentItem, TestStandard, FaultObservation, PrivacyContent, AuditEntry, RefreshToken. **Deliberate departure from the PRD schema:** readings, faults, attestations, pulsation PDF and amendment history live in `MachineTest.PayloadJson`; versions are linked `MachineTest` rows, not a `TestVersion` table; no `VendorSpecificationSnapshot`, `FinalReportBlob` or `SyncConflict` tables. Works, but note it when NZMPTA asks about the data model.
- [x] ✅ EF Core migrations current; startup retries Azure SQL serverless resume (PR #49)
- [x] ✅ Seed data — reference data (regions, processors, equipment, standards, fault observations, privacy) seeded on startup; config-driven bootstrap Super-Administrator (#22); dev users Development-only
- [x] ✅ Terms-of-use gate for testers + admin-managed privacy notices (`Pages/Account/AcceptTerms`, `Pages/Admin/Privacy`)
- [x] ✅ MPNZ brand rolled out across the shell, self-hosted fonts, raster PWA icons (PRs #28, #41, #51)
- [x] ✅ Unit + integration tests — 150 xUnit facts/theories (incl. 2 Playwright E2E) + 283 Vitest cases on `main` @ `91c673a`

---

## Phase 2 — Tester core  ·  M2 + M3  ·  🟡 ~80%

### M2 — Tester PWA core (offline + sync)
- [x] ✅ PWA installable — manifest, service worker, raster icons; **prompt-to-reload** when a new build takes over (decided 15 Sep 2026, `wwwroot/js/pwa-register.js`)
- [x] ✅ Service worker precaches the app shell assets per-URL with `cache: 'reload'`, build-stamped cache name, `ignoreSearch` for `?v=` URLs; honest 404-vs-offline handling (PR #41)
- [x] ✅ **Offline printing** — PDF generator chunks warmed while online, clear message when missing (PR #42)
- [x] ✅ IndexedDB capture store — tests, config, faults, readings, attachments, amendments; per-tester DB namespacing; purge guard fails closed (PR #39)
- [x] ✅ Reference data synced to device with version stamps — standards, equipment, fault catalog, privacy, company branding/logo, tester details, calibration dates
- [x] ✅ Company farm book cached to device; offline farm detail lookup (PR #33)
- [x] ✅ Sync — `POST /api/sync/tests` idempotent upsert by ClientId carrying config + payload; `GET /api/sync/tests?since=` delta pull with lagged watermark; continue-on-failure pushes; expired-session detection (PRs #33, #37)
- [x] ✅ Tester test history on device — push local-only, pull own + **company-wide read access** (PR #38); sync badges + "Sync now"
- [x] ✅ Offline reprint of historical tests — regenerate client-side from synced data; migrated legacy tests reprint faithfully from stored verdicts (`Client/report/legacyAdapter.ts`)
- [x] ✅ Tester-created farms go **under review** with admin approval; company-less pending farms escalate (PR #35)
- [x] ✅ Equipment calibration dates on the tester profile (PR #36)
- [x] ✅ In-app Help & guides, cached for offline (PR #60)
- [ ] ⬜ **Offline navigation / identity-free shell** — `plans/offline-tester-app.md` Phase 2: `GET /api/session`, cached identity record, `app-shell.html`, SW serves the shell for tester routes, cold launch at `/`, connectivity + pending-work indicator in the chrome. **Today `sw.js:281-282` is network-first with the offline card; every navigation needs the server.**
- [ ] ⬜ **Start a test offline** — Phase 3: client-rendered `/App/Tests/New` from the cached farm book; `/api/farms` version stamp; pre-cache milk-company logos
- [ ] ⬜ **Storage durability** — Phase 4: paginate the first pull, drop the local base64 attachment after push, `navigator.storage.persist()` + quota surfacing, cap the logo/FA caches
- [ ] ⬜ Real-device UAT on the target iPads — install, capture, airplane mode, >7 days, cold launch (never done)
- [ ] 🟡 Connectivity indicator — `useServerOnline()` drives the wizard and company list; no shell-level banner yet (lands with the shell)
- [x] ✅ Tests — sync round-trip integration (`SyncControllerTests`), Vitest on store/sync/purge, Playwright SW precache check (`ServiceWorkerCacheE2ETests`)
- [ ] ⬜ Playwright offline suite (`plans/offline-tester-app.md` §7) — cold launch, navigate, capture, print offline, PII-in-cache assertion
- ~~Offline farm creation~~ — **cut 22 Jul 2026**; online-only by design
- ~~Reference-data delta endpoint `GET /api/sync/reference-data?asOf=`~~ — superseded by per-catalogue versioned syncs; no further work
- [ ] 🟡 **Automatic sync on reconnect** — `syncAll()` runs only from the sign-off attempt and the "Sync now" buttons; the online/visibility listeners in `Client/connectivity.ts` just re-probe `/health`. A test completed offline stays device-only until the tester presses Sync (PRD story 34 wants it uploaded automatically). Trigger a sync on the online event and on tab focus when there are `local-only` tests; retry with backoff.
- [ ] ⬜ **Sync reconciliation (contracted, PRD §Decisions)** — field-level merge + a `SyncConflict` record visible in the admin portal. Today `POST /api/sync/tests` is last-writer-wins by ClientId. Becomes a real risk the moment O2 admin editing lands (tester offline edit vs admin edit of the same test), so build it with O2 — see the O2 section.

### M3 — Wizard test capture (steps 1–11)
- [x] ✅ Single offline-first Preact wizard; **ISO flowchart step order**, show-on-fail cluster step, three layouts (hub / rail / scroll) chosen from a header cog (PRs #43–#48, #56)
- [x] ✅ Machine configuration + ancillary step — full legacy field set; vacuum pumps, regulators (type + quantity + suitable-for-plant), releaser pump as lists; OEM capacity beside 8a (PRs #57, #65)
- [x] ✅ Visual faults pre-start + running — full checklists with fault dropdowns, CMM severity + recommendation auto-fill, "choice" items (cluster position, tube type)
- [x] ✅ "Check all as verified" + attestation recorded in the payload and printed
- [x] ✅ Test Record — full ISO groups 1–9 with live pass/fail; **derived readings calculated, not typed** (1c, 2d/f, 3f–h, 4c/e, 5b, 7c/f/i, 10b/d, 11b, 12b, 14b/d/f, 15b) (PR #55); decimal entry fixed
- [x] ✅ Additional tests, pulsator step (per-pulsator rows, failed-units-only tables, analyser spreads), individual cluster rows
- [x] ✅ Standards — admin-editable with device sync + update alert; ISO/manual-fixed listed read-only; legacy reference tables recovered; 14d at 35 L/min per cluster, vented-liner 12b floor (PRs #52, #64)
- [x] ✅ Fault Aggregator + Fault Summary step; failed readings get a default recommendation (PR #67)
- [x] ✅ Next Test Date (+12 months, amendments never move it) + compliance disclaimer (PRs #62, #63)
- [x] ✅ Review & sign-off — mark complete + sync, Download report, section picker, general comments; **edit-after-complete creates a linked new version with an amendment audit trail** (PRs #31, #33)
- [x] ✅ Test deletion — tester deletes own in-progress tests only
- [x] ✅ Wizard Step Resolver, Pass/Fail Calculator, Fault Aggregator — .NET + TS mirrors over shared fixtures (`tests/fixtures/*`)
- [ ] 🟡 Tester-feedback leftovers (`plans/tester-feedback-2026-09-15.md`): **4.1 oil-vs-water** observations split by pump lubrication (still one "Oil / water" item, `visualChecklist.ts:33-35`); **ISO 16 final checks** (16a vs 1a, 16b vs 2a) not captured; 15b relabel; buttons with no fault list (claw inlet/outlet, long milk tube)
- [ ] ❓ Open questions for Jono / Maria / NZMPTA: airline bends size vs length; manual "add faulty pulsator"; vented 12b minimum; 2h cleaning-reserve formula vs legacy; shell condition under Claw; oil wording; 8a verdict (Read Industrial)
- [ ] ⬜ **Repeat-farm "Next test" (PRD story 11)** — start a new test for a farm pre-populated with the machine configuration from that farm's last test. Today `/App/Tests/New` picks a farm and opens a blank wizard; `Client/ui/TestListApp.tsx` offers View/Edit only, so the whole configuration is re-keyed on every annual visit.
- [ ] ⬜ Playwright wizard happy-path (Rotary full ancillaries; Herringbone minimal) — unit coverage is strong, E2E is not

---

## Phase 3 — Reporting & admin  ·  M4 + M5 + O2 + O3  ·  🟡 ~75%

### M4 — Existing reports (PDF generation)
- [x] ✅ Engine: client-side pdfmake + pdf-lib on device (decided 5 Jun 2026); same code renders in the admin viewer
- [x] ✅ **The report, as one PDF or any selection of its sections.** On mark-complete the tester gets "Download full report" in one tap, or "Choose sections…" (`Client/wizard/ReportSectionPicker.tsx`, PR #68). The ten parts (`REPORT_PARTS` in `Client/report/testSummaryPdf.ts`): Test summary (page one: letterhead, tester block, farm/test details, fault summary + recommendations, general comments, Next Test Date, severity legend, compliance disclaimer), Machine configuration, Vacuum tests (ISO 1–9), Airflow (10–12), Individual cluster tests (13), Pulsation & ancillary (14–15), Additional tests, Visual checks, the attached analyser PDF, and attestations/amendment history (PRs #59–#68)
- [x] ✅ Six of the seven legacy reports are selectable on their own: Test Summary (page one), Test Record (= Machine configuration + ISO 1–9 + ISO 10–12), Individual Cluster Airflow (13), Pulsation System Result (14–15), Additional Testing, Visual Faults Checklist
- [ ] 🟡 **Test Report Results** — the legacy per-fault results-and-recommendations document — prints only as the fault table inside page one; there is no `results` part, so it can't be chosen without the rest of the summary. Either add it as its own part or have NZMPTA accept the merged page one (the sample PDFs sent 24 Sep show the merged form)
- [x] ✅ Equipment-not-present sections omitted (a part with nothing in it is not offered or printed)
- [x] ✅ Company logo on every page; MPNZ letterhead; NZ time stamps
- [x] ✅ Faithful reprint of migrated tests from stored verdicts
- [ ] ❓ Page count ignores pages appended by pdf-lib (noted 15 Sep; not re-verified)
- [ ] ⬜ **Golden-file PDF tests (T4)** — `testSummaryPdf.test.ts` is structural (786 lines, no snapshot/golden). Decide: normalised-byte golden vs. keep structural

### M5 — Admin portal — users, companies & reference data
- [x] ✅ Tester CRUD — list/search, create, tabbed edit, multi-role, deactivate, reset password, force-logout, certificate number, licence expiry (PRs #29, #30)
- [x] ✅ Testing Company CRUD + **report logo** upload (PNG/JPEG/SVG by bytes, ≤1 MB) on My company and Companies (PR #58)
- [x] ✅ Self-service password reset, forced reset on first login
- [x] ✅ 2FA **enrolment** — TOTP with a QR code (QRCoder) and the key as text; recovery codes shown once, regenerated by POST; "Set up a new authenticator" replaces the old one only on an explicit confirmation (PR #71)
- [x] ✅ **2FA enforcement for Super-Administrators** (PR #71, merged 7 Oct 2026; PRD story 58): an unenrolled Super-Administrator is held on the set-up page (`MfaEnrolmentMiddleware`, 403 on `/api/*`); a trusted device holds for a hard 30 days (`SlidingExpiration` off) and loses trust when the security stamp changes; a live session is also capped at 30 days from its last code (`TesterSignInManager` stamps the ticket, the middleware signs out past it); two-factor can't be switched off for the role; the JWT login refuses any account that has or must have 2FA; recovery-code sign-in page; admin **Reset two-factor**; forced reset / licence / terms gates run after the code as after the password (`SignInGates`); every outcome audited (`LoginAudit`); security stamp re-checked every minute so force sign-out and reset bite within a minute. Covered by `TwoFactorSignInTests`, `MfaEnrolmentMiddlewareTests`, `MfaSessionStampTests`, `TrustedDeviceCookieOptionsTests`; the Playwright admin signs in through the real challenge
- [ ] ❓ **Email-code fallback** for 2FA — never confirmed by NZMPTA; recovery codes and the admin reset cover the lost-device case today. Decide, or close
- [ ] 🟡 Equipment catalogue — CRUD + device sync for **8 types** (Shell, Liner, Pulsator, MilklineSize, PulsatorConfiguration, VacuumPump, ReleaserPump, Regulator). Contract says 11; ❓ confirm the remaining three with NZMPTA or close the line
- [x] ✅ Test standards — admin CRUD (accordion page), device sync, update alert
- [ ] ❓ **Vendor Specification editor with Effective Date** — standards are editable but there is no per-model spec with a scheduled effective date, and no snapshot-at-test-start. Confirm whether the current model satisfies NZMPTA; otherwise this is a schema + UI item
- [x] ✅ Standard Recommendation Wording — delivered as the **Fault Observations catalogue** (CMM severity + recommendation, admin CRUD, device sync); reading-level defaults in `Client/faults/readingRecommendations.ts`
- [ ] ⬜ Test Standard Manual upload (versioned, retained, downloadable)
- [x] ✅ Region & Milk Supply Company catalogues (CRUD, islands, logos)
- [x] ✅ Farm Details — list with filters + cross-links, tabbed edit, NZ Post autocomplete, company scoping, pending-review approval (PRs #17, #32, #35). This is the farm half of PRD story 49
- [ ] ⬜ **Company Administrator edits the final summary / recommendations on their company's tests** (PRD stories 49–50; rebuild plan Phase 9) — field-restricted (summary, recommendations, general comments), scoped to their own company, saved as a **new version** with the Company Administrator as actor and the amendment trail the tester-side edit already writes. Today the admin viewer is read-only and `Api/TestsController.cs` has GET actions only. Contracted under M5, not optional O2: build it as the first slice of the admin write path, reusing `Client/versioning/amendments.ts`
- [x] ✅ **Upcoming tests** page — farms due or overdue (PR #62); dashboard counts (pending farms, overdue tests)
- [x] ✅ Admin timestamps in NZ local time (PR #53)
- [ ] 🟡 Company-level reporting for Company Administrators — scoped test list + upcoming/overdue is all there is; ❓ confirm whether NZMPTA wants more (e.g. tests per tester, export)
- [ ] 🟡 Admin CRUD Playwright — one E2E (farm edit + NZ Post); rest covered by integration tests

### O2 — Admin test view & edit + audit
- [x] ✅ Test **list** `/admin/tests` — paginated, searchable, company-scoped (PR #26)
- [x] ✅ Test **detail** — wizard-style **read-only** render via the shared bundle (`Pages/Admin/Tests/View.cshtml`), incl. migrated tests
- [ ] ⬜ Filter chips (Tester, Company, Farm, date range, status)
- [ ] ⬜ **Edit any field → new linked version** — reuse the tester-side amendment model (`Client/versioning/amendments.ts`); needs an admin write path (today `/api/tests/{id}` is GET-only)
- [ ] ⬜ Audit panel — version timeline + per-field diffs + attestation events (data exists in the payload)
- [x] ✅ Report download from the admin viewer — the server-view branch of `ReviewSignOffStep` offers "Download report (PDF)" and `WizardApp` runs the shared generator over the server-fetched test and branding; the admin edit path just has to leave that in place
- [ ] ⬜ Soft-delete with mandatory reason (no `IsDeleted`/`DeletedAt` on `MachineTest` today)
- [ ] ⬜ **Sync reconciliation** — once admins can edit, a tester's queued offline edit and an admin edit of the same test can collide. Contracted answer (PRD §Decisions): field-level merge where the edited field sets don't overlap, last-writer-wins per field where they do, both states kept as versions, and a `SyncConflict` record surfaced in the admin list ("has conflicts" filter). Build alongside the edit path, not after it.
- [ ] ⬜ O2 Playwright path (admin edits synced test → new version → report regenerates)

### O3 — Pulsation-data PDF upload & merge into Final Report
- [x] ✅ Pulsation PDF dropzone on sign-off; stored in the payload (base64), syncs with the test, appended to the report with pdf-lib (PR #23)
- [x] ✅ Offline-capable — the attachment rides the normal sync queue
- [ ] ⬜ Admin-portal upload against a test (lands with O2 edit)
- [ ] ⬜ Server-side **Final Report store** (`/api/sync/final-report/{testId}`) — PRD says the server keeps the latest client-generated PDF for admin display. **Decision needed:** build it, or formally replace it with regenerate-on-demand in the admin viewer (above). Recommend the latter + a written note to NZMPTA.
- [ ] 🟡 Tests — Vitest covers the append path; no merge golden-file

---

## Phase 4 — Hardening & cutover  ·  M6 + O1  ·  🟡 ~30%

### O1 — Data migration tooling & cutover
- [x] ✅ Standalone console tool `tools/Migration`: `validate-source`, `dry-run --target-conn "<staging>" [--limit n]`, `cutover --target-conn "<prod>" --confirm "GO-LIVE…"` (target also via `$AUTOREP_TARGET_CONN`; `--limit` is refused on `cutover`). Documented in `tools/Migration/README.md`
- [x] ✅ Legacy read access obtained; full staging pull done 18 Jun 2026 (22,797 tests, 35 tables); 4 review findings fixed 19 Jun
- [x] ✅ Mapping: companies → testers → farms → tests + config + payload, deterministic ids; a re-run **skips** rows whose ids already exist (it does not update them — see the fresh-run item below); duplicate-GUID handling; owner-orphans to a synthetic tester; NZ time conversion; 83 company logos + certificate numbers; deleted tests excluded and reported
- [x] ✅ Quarantine + PII-redacted data-quality CSV (`data-quality.csv`; informational findings included, so not a quarantine count — see the README) + per-entity reconciliation counts (`reconciliation.csv`; the Farms row now counts farms, not referencing tests)
- [x] ✅ Cutover guard (`Pipeline/CutoverGuard.cs`): `GO-LIVE` confirm token, target must hold zero `MachineTests` (single-shot), and a server **name** containing `australiaeast` is refused
- [ ] ⬜ **Real data-residency check before cutover** — the guard's region test is a hostname sniff; `infra/modules/sql.bicep` names servers `sql-<base>` with no region in them, so a non-NZ target normally passes. Verify the target's actual Azure location (ARM lookup in the guard, or an explicit pre-check in the runbook) before the first production run
- [x] ✅ Migrated tests attributed to the migrated tester **and stamped with the company as at the test** (`MachineTest.TestingCompanyId` ← the legacy test row's `Tests.CompanyID`; the owner's current `Users.CompanyID` only when the test row's company didn't map, e.g. an unnamed company excluded from migration; an owner-orphan test keeps its company under the synthetic tester). Fixed 7 Oct 2026 in #70 — until then every migrated test would have been missing from company-scoped lists and company branding after cutover, and a tester who had moved companies would have carried their history to the new one. Reprint faithfully from stored verdicts (M2/M4). **The June staging load predates this stamp: the fresh dry-run below is what puts it right.**
- [x] ✅ Migrated testers flagged for forced password reset (`MigrationRunner` sets `ForcedPasswordResetRequired = true`); legacy-inactive accounts carry a far-future lockout
- [ ] ⬜ **NZMPTA review of the data-quality CSV** (quarantined rows, excluded deletes)
- [ ] ⬜ **Report-parity review** — sample migrated tests regenerated and compared with legacy prints (Maria)
- [ ] ❓ Map legacy `TestVaccumPumpDetails` into the new pump fields, or accept forward-only (Josh)
- [ ] ⬜ Cutover runbook (pre-checks, run, post-checks, rollback) + golden-record migration test
- [ ] ⬜ **Fresh dry-run into a cleared staging database** before the parity review and again before cutover. The runner is skip-only: after a mapping fix or a source correction, rows already in staging keep their old values and the CSV reports them as skipped, so a re-run over the June load does not validate the fix. Clear the target first (or make the runner a real upsert)

### M6 — Hardening, UAT support, security review & go-live
*Detail in `plans/m6-infra-review.md` — nothing there is ticked.*
- [ ] ⬜ **First production deploy** — `infra-prod.yml` then `app-prod.yml`; confirm every resource lands in `newzealandnorth`; prod SKUs available
- [ ] ⬜ Security review — headers/CSP (only HSTS today), CORS on `/api/*`, token policy in prod config, dependency scan, remove personal SQL admin, Key Vault RBAC, decide internal checklist vs external pen test
- [ ] ⬜ WAF / Front Door decision; geo-filter decision
- [ ] ⬜ Performance test — 20–30 concurrent sessions + sync bursts
- [ ] ⬜ Alert rules routed to Pedersen Group; availability test on `/health`; Log Analytics retention; Defender decision
- [ ] ⬜ Test restore of the SQL backup; 7-year audit retention strategy
- [ ] ⬜ Custom domain + managed certificate; manifest `start_url`/`scope` re-validated on the real host
- [ ] ⬜ `CostCentre: 'TODO'` tag; budget + cost alert; resource locks
- [ ] ⬜ Full integration pass across delivered scope (T3) + the owed T4/T5/T6 coverage
- [ ] ⬜ UAT cycles with NZMPTA — scripted scenarios; Maria Scott sign-off. Jono's 11 Sep run was the first real-world test; a second round after the offline shell is the natural gate
- [ ] ⬜ Tester onboarding comms — install PWA, first-login reset; guides exist in-app (PR #60)
- [ ] ⬜ Parallel run → DNS cutover → legacy decommission

---

## Out of contracted scope (add only by written variation)
- [ ] ⛔ **O4** Proactive notification schedule — $2,500. *Note:* the Upcoming tests page (PR #62) already surfaces due/overdue farms to admins; O4 would add the emails.
- [ ] ⛔ **O5** Vendor self-service portal — $3,500

> **O3** formal written variation + final price — ❓ still to confirm with NZMPTA.

## Cross-cutting
- [x] ✅ Automated suite — 150 xUnit (unit + `WebApplicationFactory` integration + 2 Playwright) and 283 Vitest on `main` @ `91c673a`; CI runs all of it on every PR. Still owed: golden-file PDF, wizard E2E, offline E2E. See `plans/test-schedule.md` (its execution record stops at 5 Jun 2026 — refresh it with the current run).
- [x] ✅ Audit logging of admin actions
- [x] ✅ Brand assets applied (MPNZ)
- [ ] ⬜ Accessibility baseline (semantic HTML, ARIA, keyboard, contrast) — never assessed
- [ ] ⬜ `plans/test-schedule.md` execution record refreshed; `tools/Migration/README.md` command table corrected

## Decisions and open items (for NZMPTA / Josh)
- **Server-side Final Report store** — build, or replace with regenerate-on-demand (recommended).
- **Vendor Specification Effective Date / snapshot-at-test-start** — not built as specified; confirm the standards model is sufficient.
- **Equipment types** — 8 built vs 11 in the contract line; name the missing three or close.
- **2FA mechanism** (TOTP only vs email fallback) — never confirmed; TOTP enforcement for Super-Administrators shipped in PR #71 regardless. Confirm whether an email-code fallback is still wanted.
- **Farm details propagate vs snapshot** — still the propagate model: editing a farm updates it for every test of that farm. *(The sibling question about test company ownership is resolved: `MachineTest.TestingCompanyId` is stamped on first upload and deliberately never re-stamped, so a tester who changes company does not take their history with them — `CompanyTestVisibilityTests`.)*
- **Tester-feedback open questions** — see `plans/tester-feedback-2026-09-15.md` §Open questions.

## History
- **5 Jun 2026** — first assessment @ `fe83d8e`: ~20–25%. Engine decision (client-side PDF), prod region corrected to NZ North, farm schema + NZ Post built.
- **8–11 Jun 2026** — wizard, pass/fail, faults, first report, O3 append, admin catalogues.
- **18–25 Jun 2026** — O1 tool + staging pull, legacy reprint, privacy gate, admin test viewer, versioning, brand rollout.
- **Jul 2026** — sync hardening, offline printing, licence sync-only session, farm review, calibration, offline-app plan.
- **Aug 2026** — layouts, legacy standards, NZ times, serverless resume retry.
- **Sep 2026** — tester feedback (Jono, 11 Sep run): calculated readings, flowchart order, pumps/regulators; report redesign (page one, letterhead, logo, disclaimer, next test date; section picker to print the whole report or chosen sections); help guides; upcoming tests; prod workflow fixes.
- **7 Oct 2026** — this reassessment @ `91c673a`; GitHub phase issues #1–#12 closed in favour of this file. Same day: 2FA enforcement for Super-Administrators (#71), the admin home 500 on SQL Server fixed (#72), and the migration tool's company stamp, farm units and `--limit` guard (#70).
