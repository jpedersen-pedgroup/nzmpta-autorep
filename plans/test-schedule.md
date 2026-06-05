# NZMPTA AutoRep — Test Schedule

> **Status: LIVING DOCUMENT — v0.1 (5 Jun 2026).** Two sections are deliberately stubbed and will be filled in when the inputs arrive:
> - 🔓 **Wizard test cases** — pending the half-day workshop with 2–3 experienced testers (validates Section 7.2 wizard steps).
> - 🔓 **Migration test cases** — pending read access to the legacy Azure SQL database + schema copy.
>
> Source of truth for *what* to test: `PRD.md` (§Testing Decisions) and `plans/build-checklist.md`. This schedule maps testing to the **4 contract delivery phases** and defines the **test gate** each must pass before NZMPTA Acceptance.

## 1. Current state
The test **harness is stood up** (5 Jun 2026): xUnit unit tests + a `WebApplicationFactory` integration fixture on an InMemory store (`tests/Autorep.Web.Tests`), **10 tests green** — address parsing (T1), `/health`, anonymous auth-gating on `/Admin/Farms` and the address proxy, and reference-data seeding (T3). `Program` skips the SQL Server provider in the `Testing` environment so tests run on InMemory. Still to build out: golden-file PDF (T4), Playwright (T5), and broader coverage as features land.

## Test execution record — evidence of completed testing

> A dated record of test runs, kept as evidence for NZMPTA that testing is performed throughout the build. Reproducible any time with `dotnet test`; a machine-readable TRX is produced via `dotnet test --logger trx` (`tests/Autorep.Web.Tests/TestResults/`). This record grows as coverage is added each phase.

**Latest run**

| | |
|---|---|
| Date / time | Fri 5 Jun 2026, 21:11 NZST |
| Build under test | commit `fd82a44` (branch `claude/distracted-johnson-b224ac`) |
| Framework / tooling | .NET 9 · xUnit 2.9 · WebApplicationFactory (InMemory) |
| Command | `dotnet test` |
| **Result** | ✅ **PASS — 10 / 10 automated tests** |

**Automated tests**

| # | Test | Type | Result |
|---|---|---|---|
| 1 | Address parsing — strip postcode from the NZ Post city line (6 cases: Wellington, Auckland, Palmerston North, Christchurch, null, blank) | Unit | ✅ Pass |
| 2 | Health endpoint returns 200 | Integration | ✅ Pass |
| 3 | `/Admin/Farms` redirects an unauthenticated user to login (access control enforced) | Integration | ✅ Pass |
| 4 | Address autocomplete proxy rejects unauthenticated requests (admin-only) | Integration | ✅ Pass |
| 5 | Reference data seeded on startup (16 NZ regions + dairy processors) | Integration | ✅ Pass |

**Manual / exploratory verification** (local, against LocalDB + live NZ Post, 5 Jun 2026)

| # | Check | Result |
|---|---|---|
| 1 | App boots; database migrations apply; reference data seeds; `/health` = Healthy | ✅ Pass |
| 2 | Administrator sign-in (cookie authentication) | ✅ Pass |
| 3 | NZ Post address suggestions returned for a query (authenticated) | ✅ Pass |
| 4 | NZ Post details parse to Address / Town / Post code (e.g. "Wellington 6011" → town "Wellington", post code "6011") | ✅ Pass |
| 5 | `/Admin/Farms` list renders; Company-Administrator scoping query executes on SQL Server | ✅ Pass |
| 6 | Farm Details edit page renders with Region & Milk-company pickers and address autocomplete | ✅ Pass |

**Coverage note.** This is the test harness plus the first automated tests (foundation, access control, reference data, address handling). Coverage expands each phase per the gates below — notably the golden-file PDF report tests, the wizard Pass/Fail and Step-Resolver unit suites, and Playwright happy-paths. Out-of-automated-scope items (PWA offline lifecycle, browser matrix) are verified during UAT.

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
