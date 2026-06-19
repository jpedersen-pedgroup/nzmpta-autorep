# AutoRep O1 — data-migration tool (`autorep-migrate`)

Standalone .NET console tool that migrates the legacy AutoRep SQL database into the new EF Core
schema. It is **not** wired into the web request path — it references the web app only to reuse the
`AutorepDbContext` + entities for writes (added in the mapper increment).

## Source → target

- **Source:** legacy `Autorep_bak` (SQL Server). 22,797 tests across 35 tables; `Tests` header +
  satellites joined by `satellite.TestGuid = Tests.GUID` (the `IDNUMBER`/`TestID` columns are a
  per-company counter — never join on them).
- **Target:** the new EF schema. Dry-runs write to the Azure **staging** DB; cutover writes to
  production behind a guard.

All readings/faults/pulsation land in `MachineTest.PayloadJson` (the new schema has no reading
child tables). Original verdict codes are preserved so reprints are faithful without recomputation.

## Commands

| Command | Status | What it does |
|---|---|---|
| `validate-source` | ✅ implemented | Read-only pre-flight: row counts, duplicate-GUID hazards, satellite join-key integrity, identity/branding sources. Modifies nothing. |
| `dry-run --target staging` | ⬜ next increment | Idempotent upsert into staging; emits a PII-redacted data-quality CSV + quarantine. |
| `cutover --target production --confirm "<token>"` | ⬜ next increment | Single-shot guarded production run (empty-target + confirm token + NZ-region + PII-gate checks). |

```bash
# from the repo root
dotnet run --project tools/Migration -- validate-source
dotnet run --project tools/Migration -- validate-source --conn "Server=...;Database=Autorep_bak;..."
```

Connection string resolution: `--conn`, then `$AUTOREP_LEGACY_CONN`, then the local default
(`localhost,1433` / `Autorep_bak` / Integrated Security).

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
