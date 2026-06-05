# NZMPTA AutoRep — Test Schedule

> **Status: LIVING DOCUMENT — v0.1 (5 Jun 2026).** Two sections are deliberately stubbed and will be filled in when the inputs arrive:
> - 🔓 **Wizard test cases** — pending the half-day workshop with 2–3 experienced testers (validates Section 7.2 wizard steps).
> - 🔓 **Migration test cases** — pending read access to the legacy Azure SQL database + schema copy.
>
> Source of truth for *what* to test: `PRD.md` (§Testing Decisions) and `plans/build-checklist.md`. This schedule maps testing to the **4 contract delivery phases** and defines the **test gate** each must pass before NZMPTA Acceptance.

## 1. Current state
The test **harness is stood up** (5 Jun 2026): xUnit unit tests + a `WebApplicationFactory` integration fixture on an InMemory store (`tests/Autorep.Web.Tests`), **10 tests green** — address parsing (T1), `/health`, anonymous auth-gating on `/Admin/Farms` and the address proxy, and reference-data seeding (T3). `Program` skips the SQL Server provider in the `Testing` environment so tests run on InMemory. Still to build out: golden-file PDF (T4), Playwright (T5), and broader coverage as features land.

## Test execution record — evidence of completed testing

> A dated record of test runs, kept as evidence for NZMPTA that testing is performed throughout the build. Grouped by **delivery phase** and tagged with the **test type** (T-numbers from §2). Reproducible with `dotnet test`; CI publishes a results summary (job summary) and a machine-readable TRX artifact on every run. This record grows as coverage is added each phase.

**Latest run**

| | |
|---|---|
| Date / time | Fri 5 Jun 2026, 21:11 NZST |
| Build under test | commit `fd82a44` (branch `claude/distracted-johnson-b224ac`) |
| Framework / tooling | .NET 9 · xUnit 2.9 · WebApplicationFactory (InMemory) |
| Command | `dotnet test` (CI: a unit/integration job + a separate Playwright E2E job, each → TRX artifact + job-summary table) |
| **Result** | ✅ **PASS — 11 / 11 automated tests** (incl. 1 Playwright E2E; plus 6 manual checks, all pass) |

### Phase 1 — Foundation (M1)

| Test ID | Type | Test | Result |
|---|---|---|---|
| P1-01 | T3 Integration | Health endpoint returns 200 | ✅ Pass |
| P1-02 | T3 Integration | Admin area enforces authentication (`/Admin/Farms` → login) | ✅ Pass |
| P1-03 | Manual | App boots; migrations apply; reference data seeds; `/health` = Healthy | ✅ Pass |
| P1-04 | Manual | Administrator sign-in (cookie authentication) | ✅ Pass |

### Phase 3 — Reporting & admin (M4 · M5 · O2)

| Test ID | Type | Test | Result |
|---|---|---|---|
| P3-01 | T1 Unit | Address parsing — strip postcode from the NZ Post city line (6 cases: Wellington, Auckland, Palmerston North, Christchurch, null, blank) | ✅ Pass |
| P3-02 | T3 Integration | Reference data seeded on startup (16 NZ regions + dairy processors) | ✅ Pass |
| P3-03 | T3 Integration | Address autocomplete proxy rejects unauthenticated requests (admin-only) | ✅ Pass |
| P3-04 | Manual | NZ Post address suggestions returned for a query (authenticated) | ✅ Pass |
| P3-05 | Manual | NZ Post details parse to Address / Town / Post code ("Wellington 6011" → town "Wellington", post code "6011") | ✅ Pass |
| P3-06 | Manual | `/Admin/Farms` list renders; Company-Administrator scoping query executes on SQL Server | ✅ Pass |
| P3-07 | Manual | Farm Details edit page renders with Region & Milk-company pickers and address autocomplete | ✅ Pass |
| P3-08 | T5 E2E | (Playwright, real browser) Admin signs in → opens a farm → NZ Post autocomplete (stubbed) fills Address/Town/PostCode → saves successfully | ✅ Pass |

### Phases not yet exercised
- **Phase 2** — Tester core (M2) & Wizard (M3): pending build. To come: T1/T2 Pass/Fail Calculator & Wizard Step Resolver (.NET + TS parity), T5 wizard happy-path.
- **Phase 4** — Hardening (M6) & migration (O1): pending. To come: T4 golden-file PDF, T7 migration validation, T8 security review, T9 performance, T10 UAT.

*Test ID = phase-scoped reference; Type = test category from §2 (T1–T10) or a manual/exploratory check.*

## 2. Test types & tooling

| # | Test type | What it covers | Tool | Runs in CI? |
|---|---|---|---|---|
| T1 | Unit (.NET) | Deep modules: Pass/Fail Calculator, Wizard Step Resolver, Fault Aggregator, Sync Reconciliation, Test Versioning, Reference Data Snapshot | xUnit + FluentAssertions | Yes |
| T2 | Unit (TypeScript) | TS mirrors (Pass/Fail, Wizard Step Resolver) + report modules; shared fixtures must agree with .NET | Vitest | Yes |
| T3 | Integration (.NET) | Sync API round-trips, Audit interceptor in-transaction, auth/refresh/licence pipeline | `WebApplicationFactory` + EF (test DB) | Yes |
| T4 | Golden-file PDF | Each named report renders byte-equivalent to a checked-in expected PDF (normalised) | Vitest (Node) — **client-side pdfmake/pdf-lib** | Yes |
| T5 | E2E / Playwright | Wizard happy paths + Admin portal CRUD + O2 edit→regenerate | Playwright | Yes (smoke) |
| T6 | Offline / PWA | Service worker + IndexedDB lifecycle, offline create → reconnect → sync | Manual (UAT + pilot) | No |
| T7 | Migration validation | Legacy → new mapping, quarantine, idempotent dry-run, sample-report parity | Golden-record + manual review | Partial |
| T8 | Security review | TLS, private endpoint, secrets, authZ, headers, lockout, token lifecycle | Manual + checklist (M6) | No |
| T9 | Performance | 20–30 concurrent sessions, sync under load | Load script (M6) | No |
| T10 | UAT | Scripted tester + admin scenarios; Maria Scott sign-off | Manual (NZMPTA) | No |

## 3. Schedule by delivery phase (test gates)

### Phase 1 — Foundation (M1) — *gate before Phase 1 Acceptance*
- [ ] T3: Audit interceptor writes AuditEntry in the same transaction; failed write ⇒ no audit row
- [ ] T3: `POST /api/auth/login` issues tokens; refresh rotates + rejects replay; expired licence blocks login
- [ ] T3: role-gating returns 403 across roles
- [ ] T3: `POST /api/sync/tests` upsert idempotency
- [ ] T5: one smoke test — login → create test → admin sees it
- [ ] CI green on PR (build + tests)

### Phase 2 — Tester core (M2, M3) — *gate before Phase 2 Acceptance*
- [ ] T1/T2: Wizard Step Resolver — .NET & TS agree on every fixture (min-config, fully-loaded, pathological)
- [ ] T1/T2: Pass/Fail Calculator — .NET & TS agree; boundary + `NoStandardAvailable` (Other)
- [ ] T1: Fault Aggregator — zero/one/many faults, each section, each severity, grouping
- [ ] T3: reference-data delta sync (past/future/today effective dates)
- [ ] T5: wizard happy path (Rotary full ancillaries; Herringbone minimal) incl. "Check all as verified"
- [ ] T6: offline create → reconnect → sync (manual)
- 🔓 **Wizard test cases — to be expanded after tester workshop** (see §4)

### Phase 3 — Reporting & admin (M4, M5, O2) — *gate before Phase 3 Acceptance*
- [ ] T4: golden-file PDF per report (7); equipment-not-present sections absent on min-config fixture
- [ ] T4: Vacuum Pump Speed/Capacity populated (targeted regression)
- [ ] T5: Admin CRUD for each managed entity; audit entries written
- [ ] T1: Test Versioning Engine — every edit ⇒ new immutable version + before/after audit
- [ ] T5: O2 — edit synced test ⇒ Final Report regenerates (golden-file diff vs original)

### Phase 4 — Hardening & cutover (M6, O1) — *gate before Go-Live*
- [ ] T7: migration golden-record (known legacy snapshot ⇒ expected schema + data-quality CSV); idempotent re-run
- [ ] T7: sample migrated tests regenerate reports matching legacy originals (NZMPTA review)
- [ ] T8: security review checklist signed off
- [ ] T9: performance at 20–30 concurrent sessions within targets
- [ ] T3: full integration suite green across delivered scope
- [ ] T10: UAT scenarios passed; Maria Scott sign-off
- 🔓 **Migration test cases — to be expanded after legacy SQL access** (see §5)

## 4. 🔓 Wizard test cases (placeholder — fill after tester workshop)
Once the wizard workflow (Requirements §7.2) is validated with 2–3 experienced testers, expand here:
- [ ] Per-step field lists + validation rules → unit/E2E cases
- [ ] Machine-configuration → visible-steps truth table → Wizard Step Resolver fixtures
- [ ] Standards thresholds per equipment type → Pass/Fail Calculator fixtures
- [ ] "Check all as verified" attestation wording per checklist (Pre-Start / Running / config)
- [ ] Representative real-world machines (Rotary / Herringbone variants) as E2E personas

## 5. 🔓 Migration test cases (placeholder — fill after legacy SQL schema)
Once the legacy Azure SQL schema + sample data are available, expand here:
- [ ] Legacy table → new entity field mapping table (the migration spec)
- [ ] Quarantine cases (missing required, broken FK, malformed) with expected error reasons
- [ ] Row-count reconciliation per table (migrated vs quarantined)
- [ ] Golden-record snapshot (anonymised legacy subset) for the migration test
- [ ] Sample tests for report-parity review (which Test IDs, expected outputs)

## 6. Open items blocking full test definition
| Blocker | Needed for | Owner |
|---|---|---|
| Wizard workshop with testers | §4 wizard cases, M3 gate | NZMPTA to nominate testers |
| Legacy SQL read access + schema | §5 migration cases, O1 gate | NZMPTA |
| ~~Report engine~~ — **RESOLVED 5 Jun: client-side** pdfmake/pdf-lib (offline print) | T4 golden-file approach | ✔ |
| Brand assets | report golden-file baselines | NZMPTA |

## 7. Conventions
- Golden files normalised (strip generation timestamps; embed fonts) before byte comparison.
- Shared fixture-set is the contract between each .NET module and its TS mirror — both must pass identical cases.
- "Out of automated scope" (manual UAT/pilot): PWA offline lifecycle, browser-compatibility matrix, visual regression of the PWA shell.
