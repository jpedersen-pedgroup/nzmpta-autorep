# AutoRep O1 — data-migration tool (`autorep-migrate`)

Standalone .NET console tool that migrates the legacy AutoRep SQL database into the new EF Core
schema. It is **not** wired into the web request path — it references the web app only to reuse the
`AutorepDbContext` + entities for writes.

## Source → target

- **Source:** legacy `Autorep_bak` (SQL Server). 22,797 tests across 35 tables; `Tests` header +
  satellites joined by `satellite.TestGuid = Tests.GUID` (the `IDNUMBER`/`TestID` columns are a
  per-company counter — never join on them).
- **Target:** the new EF schema. Dry-runs write to the Azure **staging** DB; cutover writes to
  production behind a guard.

All readings/faults/pulsation land in `MachineTest.PayloadJson` (the new schema has no reading
child tables). Original verdict codes are preserved so reprints are faithful without recomputation.

## Commands

All three are implemented (`Program.cs`). `dry-run` and `cutover` run the same pipeline
(`Pipeline/MigrationRunner.cs`); the only differences are the guard and the `Cutover` flag.

| Command | What it does |
|---|---|
| `validate-source` | Read-only pre-flight against the legacy DB: row counts, duplicate-GUID hazards, satellite join-key integrity, identity/branding sources. Modifies nothing. |
| `dry-run` | Loads the target (staging): companies → testers → farms → tests + config + payload, in FK order. Every target row is keyed by a deterministic id derived from its legacy identity, and a row whose id already exists is **skipped, not updated** — so a re-run never duplicates, but it also never corrects: after a mapping fix or a source correction, clear the target and run again, or the stale rows stay and the CSV merely reports them as skipped. Writes the data-quality CSV and the reconciliation CSV. |
| `cutover --confirm "GO-LIVE…"` | The same run against production, behind `Pipeline/CutoverGuard.cs`: refuses unless the confirm token starts with `GO-LIVE`, the target server **name** does not contain `australiaeast`, and the target holds **zero** `MachineTests`. Single-shot by construction — a second run is refused by the empty-target check, which is also why `--limit` is rejected here. **The region test is a hostname sniff, not a lookup of the server's Azure location** (the Bicep names servers `sql-<base>` with no region in them), so confirm the target's location yourself before running — see the O1 items in `plans/build-checklist.md`. |

### Options

| Option | Applies to | Meaning |
|---|---|---|
| `--conn "<legacy conn>"` | all | Legacy source. Falls back to `$AUTOREP_LEGACY_CONN`, then the local default (`localhost,1433` / `Autorep_bak` / Integrated Security). |
| `--target-conn "<target conn>"` | `dry-run`, `cutover` | Target database. Falls back to `$AUTOREP_TARGET_CONN`. Required — exit code 2 if neither is set. |
| `--confirm "<token>"` | `cutover` | Must start with `GO-LIVE`. Exit code 3 if the guard refuses. |
| `--out <dir>` | `dry-run`, `cutover` | Where the CSVs go. Default: `migration-output/` beside the built binary. |
| `--limit <n>` | `dry-run` only | Process only the first *n* logical tests (companies, testers and farms still migrate in full), for quick smoke runs. Refused with `cutover` (exit code 2): a partial production load would then be locked in by the empty-target guard. |

Options take `--key value` or `--key=value`.

```bash
# from the repo root
dotnet run --project tools/Migration -- validate-source
dotnet run --project tools/Migration -- validate-source --conn "Server=...;Database=Autorep_bak;..."

# dry-run into staging (target via env var or --target-conn), first 200 tests only
dotnet run --project tools/Migration -- dry-run --target-conn "Server=...staging...;" --limit 200 --out ./migration-output

# production cutover — guarded; needs an empty target in newzealandnorth
dotnet run --project tools/Migration -- cutover --target-conn "Server=...prod...;" --confirm "GO-LIVE 2026-xx-xx"
```

### Outputs (in `--out`)

- `data-quality.csv` — row-level quarantine: `LegacyTable,LegacyKey,TargetEntity,Reason,Severity`.
  **PII-redacted by design** (`Pipeline/Quarantine.cs`): only the legacy key and a reason code, never
  names, emails, addresses or phone numbers, so it is safe to share with NZMPTA. Sorted, so an
  unchanged source produces an identical file on re-run.
- `reconciliation.csv` — per-entity counts (source rows, migrated, skipped as already present,
  quarantined), also printed to the console.

### Migrated accounts

Every migrated Tester gets `ForcedPasswordResetRequired = true`, so legacy credentials cannot be
used against the new platform. Legacy-inactive accounts carry a far-future `LockoutEnd`, mirroring
the admin "deactivate", so a password reset does not quietly reactivate them.

## Status (7 Oct 2026)

- Tool complete; full dry-run of all 22,797 tests into staging done 18 Jun 2026, four review
  findings fixed 19 Jun.
- Still to do before cutover, tracked in `plans/build-checklist.md` (O1): NZMPTA review of
  `data-quality.csv`, report-parity review of sample migrated tests against legacy prints, a fresh
  dry-run on the final schema, the cutover runbook (pre-checks, run, post-checks, rollback), and the
  decision on mapping legacy `TestVaccumPumpDetails` into the new pump fields.

## Migration decisions (locked 18 Jun 2026)

- **Deleted tests (707, `IsDelete=1`): excluded** — reported in the data-quality CSV, not migrated.
- **Branding: added** — `TestingCompany.LogoData`/`LogoContentType` + `Tester.CertificateNo`; the 83
  legacy company logos and tester certificate numbers are migrated for faithful reprints.
- **Reprint verdicts: trust stored** — reprints redisplay the original pass/fail; the 2-axis legacy
  standards grids (`EffectiveArea`/`ReserveReceiver`/`MinSpeedPowerCal`) are NOT migrated because the
  new engine computes that logic in code (see `Client/passfail/standards.ts`).

## Baked-in corrections (from the adversarial design review)

- Split duplicate-GUID groups: collapse true sync re-inserts, but ~72 groups are **distinct tests**
  (different `TestNo`/date) and get distinct `ClientId`s.
- Faults & visual faults join by `TestGuid` (verified clean), not a `TestID` tuple.
- `PlantType`: 1 = Highline herringbone, 2 = Lowline herringbone, 3 = Rotary.
- Owner-orphan tests (23) salvaged via a synthetic "Legacy/Unknown Tester", not dropped.
- Dates converted with `TimeZoneInfo` (NZ), not a fixed UTC offset.
