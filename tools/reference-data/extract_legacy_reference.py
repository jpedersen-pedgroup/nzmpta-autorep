#!/usr/bin/env python3
"""
Extract the reference/standards catalogues out of the legacy Autorep database.

Background (see plans/reference/standards-audit.md): the per-model pulsator bands, the OEM vacuum
pump curves and the releaser speed/power table were recorded for months as "blocked on NZMPTA,
manual pages are image-only scans". They are not blocked -- every one of those numbers is already
in Autorep_bak. Only the *text extraction* of the manual was missing them. This script pulls them
out so they can be shipped as reference data instead of re-keyed from scans.

Outputs (JSON, sorted, stable key order so re-runs produce clean diffs):

  src/Autorep.Web/Client/reference/     -- bundled to the device, kept small
    pulsatorBands.json        127 models: rate/ratio bands, phase b/d, max chamber vacuum
    vacuumPumps.json          140 models: min/max RPM, airflow per RPM, motor size factor
    milkPumps.json             45 models: min/max, size, motor
    releaserSpeedPower.json    75 rows:   clusters x heads -> power, minimum speed
    reserveReceiver.json       12 rows:   milkline diameter x working vacuum -> required reserve

The legacy LinerShellMatching / LinerCupNippleMatching matrices are deliberately NOT extracted:
shell/liner and jetter compatibility stay a manual tester judgement (Josh, 27 Aug 2026) via the
visual-checklist items, so shipping 6,000 matrix rows would be dead weight.

Connection: the legacy DB restored locally on SQLEXPRESS (TCP 1433, Windows auth). Override with
    --server / --database if yours differs.

Requires pyodbc and an "ODBC Driver NN for SQL Server".
"""
import argparse
import json
import os

import pyodbc

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
CLIENT_REF = os.path.join(REPO, "src", "Autorep.Web", "Client", "reference")

# (filename, destination dir, SQL). Column aliases are the JSON keys, so the shape of each file
# is defined here and nowhere else.
QUERIES = [
    ("pulsatorBands.json", CLIENT_REF, """
        SELECT PulsatorName        AS name,
               PULBrand            AS brand,
               PULRateMin          AS rateMin,
               PULRateMax          AS rateMax,
               PULRatioMin         AS ratioMin,
               PULRatioMax         AS ratioMax,
               PULB                AS phaseB,
               PULD                AS phaseD,
               MaxChamberVacuum    AS maxChamberVacuum,
               PULRecommendedpulsationrate  AS recommendedRate,
               PULRecommendedpulsationratio AS recommendedRatio,
               PULMaximumclustersperpulsator AS maxClustersPerPulsator
        FROM Pulsator ORDER BY PULBrand, PulsatorName
    """),
    ("vacuumPumps.json", CLIENT_REF, """
        SELECT VPMake          AS make,
               VPModelName     AS model,
               MinRPM          AS minRpm,
               MaxRPM          AS maxRpm,
               AirFlow         AS airFlow,
               MotorSizeFactor AS motorSizeFactor,
               WaterFlowRate   AS waterFlowRate
        FROM VPModel ORDER BY VPMake, VPModelName
    """),
    ("milkPumps.json", CLIENT_REF, """
        SELECT MPMake      AS make,
               MPModelName AS model,
               MPMin       AS minValue,
               MPMax       AS maxValue,
               MPSize      AS size,
               MPMotor     AS motor
        FROM MilkPumps ORDER BY MPMake, MPModelName
    """),
    ("releaserSpeedPower.json", CLIENT_REF, """
        SELECT ClusterNumber AS clusters,
               NumberHeads   AS heads,
               Power         AS power,
               MinSpeed      AS minSpeed
        FROM MinSpeedPowerCal
        ORDER BY TRY_CAST(ClusterNumber AS int), TRY_CAST(NumberHeads AS int)
    """),
    ("reserveReceiver.json", CLIENT_REF, """
        SELECT MilklineDiameter AS milklineDiameter,
               WorkingVacuum    AS workingVacuum,
               ReserveReceiver  AS requiredReserve
        FROM ReserveReceiver
        ORDER BY MilklineDiameter, WorkingVacuum
    """),
]


# Two pulsator names are wrong in the legacy table and were silently corrected when
# Client/reference/pulsators.json was first built -- "Confif" is a typo for "Config", and the
# other is missing a space. The shipped catalogue is what testers see, so its spelling wins;
# without this the bands for those two models would not join to the catalogue by name.
PULSATOR_NAME_FIXES = {
    "W Pneumatic Confif 2": "W Pneumatic Config 2",
    "DeLaval EP 100/100B 2 clusters(50)": "DeLaval EP 100/100B 2 clusters (50)",
}


def pick_driver():
    drivers = [d for d in pyodbc.drivers() if "ODBC Driver" in d and "SQL Server" in d]
    if not drivers:
        raise SystemExit("No 'ODBC Driver NN for SQL Server' found. Install the Microsoft ODBC driver.")
    return sorted(drivers)[-1]


def clean(value):
    """Trim strings and collapse internal whitespace; leave other types as the driver typed them.

    Collapsing matters: 16 of the 127 pulsator names carry a double space in the legacy table
    ("Autopuls P or P2  Config 1"), while the catalogue the wizard already ships
    (Client/reference/pulsators.json) has them single-spaced. Joining the bands to the catalogue
    by name silently drops those 16 models unless both sides normalise the same way.
    """
    return " ".join(value.split()) if isinstance(value, str) else value


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--server", default="localhost,1433")
    ap.add_argument("--database", default="Autorep_bak")
    args = ap.parse_args()

    cs = (
        f"DRIVER={{{pick_driver()}}};SERVER={args.server};DATABASE={args.database};"
        "Trusted_Connection=yes;TrustServerCertificate=yes"
    )
    print(f"Connecting to {args.server}/{args.database} ...")
    with pyodbc.connect(cs, timeout=10) as conn:
        cur = conn.cursor()
        for filename, dest, sql in QUERIES:
            cur.execute(sql)
            cols = [c[0] for c in cur.description]
            rows = [{c: clean(v) for c, v in zip(cols, r)} for r in cur.fetchall()]
            for row in rows:
                if "name" in row and row["name"] in PULSATOR_NAME_FIXES:
                    row["name"] = PULSATOR_NAME_FIXES[row["name"]]
            os.makedirs(dest, exist_ok=True)
            path = os.path.join(dest, filename)
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                json.dump(rows, f, indent=2, ensure_ascii=False)
                f.write("\n")
            print(f"  {len(rows):>5} rows -> {os.path.relpath(path, REPO)}")
    print("Done.")


if __name__ == "__main__":
    main()
