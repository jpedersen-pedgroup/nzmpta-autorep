using Microsoft.Data.SqlClient;

namespace Nzmpta.AutoRep.Migration.Source;

/// <summary>
/// Read-only pre-flight validation of the legacy AutoRep database. Re-verifies the assumptions the
/// O1 migration design depends on (canonical GUID join key, duplicate-GUID handling, exclusions,
/// branding sources) directly against the live DB. Runs only SELECTs — modifies nothing.
/// Expected values in parentheses are the figures from the 18 Jun 2026 schema analysis; large
/// drift means the source has changed and the design should be re-checked before a real run.
/// </summary>
public static class SourceValidator
{
    public static async Task<int> RunAsync(string connectionString)
    {
        Console.WriteLine("AutoRep legacy source — pre-flight validation (READ-ONLY)");
        Console.WriteLine(new string('=', 70));

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            Console.WriteLine($"Connected: {conn.DataSource} / {conn.Database}");

            await HeaderProbe(conn);
            await DuplicateGuidProbe(conn);
            await OwnerOrphanProbe(conn);
            await SatelliteJoinKeyProbe(conn);
            await IdentityAndBrandingProbe(conn);

            Console.WriteLine();
            Console.WriteLine("Pre-flight complete — no data was modified.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FATAL: pre-flight could not complete: {ex.Message}");
            return 1;
        }
    }

    private static async Task HeaderProbe(SqlConnection conn)
    {
        Section("Tests header");
        try
        {
            const string sql = @"
SELECT
  COUNT(*)                                              AS TotalTests,
  COUNT(DISTINCT [GUID])                                AS DistinctGuids,
  SUM(CASE WHEN IsDelete = 1 THEN 1 ELSE 0 END)         AS DeletedTests,
  SUM(CASE WHEN TestDate IS NULL THEN 1 ELSE 0 END)     AS NullTestDate,
  CONVERT(varchar(10), MIN(TestDate), 23)              AS MinDate,
  CONVERT(varchar(10), MAX(TestDate), 23)              AS MaxDate,
  COUNT(DISTINCT UserID)                                AS DistinctUsers,
  COUNT(DISTINCT CompanyID)                             AS DistinctCompanies
FROM dbo.Tests;";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                Stat("Total tests", L(r, "TotalTests"), "22,797");
                Stat("Distinct GUIDs", L(r, "DistinctGuids"), "22,308");
                Stat("Soft-deleted (IsDelete=1)", L(r, "DeletedTests"), "707 — EXCLUDED from migration");
                Stat("Null TestDate", L(r, "NullTestDate"), "0");
                Stat("Test date range", $"{S(r, "MinDate")} .. {S(r, "MaxDate")}", "2011-07-18 .. 2026-06-05");
                Stat("Distinct UserID (testers)", L(r, "DistinctUsers"), "185");
                Stat("Distinct CompanyID", L(r, "DistinctCompanies"), "75");
            }
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static async Task DuplicateGuidProbe(SqlConnection conn)
    {
        Section("Duplicate-GUID analysis (idempotency-key hazard)");
        try
        {
            const string sql = @"
WITH g AS (
  SELECT [GUID] AS gid, COUNT(*) AS n, COUNT(DISTINCT TestNo) AS distinctTestNo
  FROM dbo.Tests
  GROUP BY [GUID]
  HAVING COUNT(*) > 1
)
SELECT
  COUNT(*)                                                       AS DupGroups,
  ISNULL(SUM(n), 0)                                              AS DupRows,
  ISNULL(SUM(CASE WHEN distinctTestNo > 1 THEN 1 ELSE 0 END), 0) AS DistinctTestGroups,
  ISNULL(SUM(CASE WHEN distinctTestNo > 1 THEN n ELSE 0 END), 0) AS DistinctTestRows
FROM g;";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 };
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                Stat("Duplicate-GUID groups", L(r, "DupGroups"), "478 (mostly sync re-inserts)");
                Stat("Rows in those groups", L(r, "DupRows"), "967");
                Stat("Groups = DISTINCT tests", L(r, "DistinctTestGroups"), "~72  <-- must NOT collapse");
                Stat("Rows in distinct-test groups", L(r, "DistinctTestRows"), "~149");
                Console.WriteLine("  => ClientId must key on (legacy GUID + TestNo), not GUID alone, or ~72 real tests are lost.");
            }
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static async Task OwnerOrphanProbe(SqlConnection conn)
    {
        Section("Owner-orphan tests (UserID with no Users row)");
        try
        {
            const string sql = @"
SELECT COUNT(*)
FROM dbo.Tests t
WHERE t.UserID IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.ID = t.UserID);";
            var orphans = await ScalarLong(conn, sql);
            Stat("Tests with orphan UserID", orphans, "23 — salvaged via synthetic 'Legacy/Unknown Tester'");
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static async Task SatelliteJoinKeyProbe(SqlConnection conn)
    {
        Section("Satellite join-key integrity (TestGuid -> Tests.GUID)");
        Console.WriteLine("  Near-zero orphans confirm GUID is the canonical key for every satellite.");
        (string Table, string Col)[] satellites =
        {
            ("TestDairyFarmInfo", "TestGuid"),
            ("MMTestSummary", "TestGuid"),
            ("MMTestRecords1", "TestGuid"),
            ("MMTestRecords2", "TestGuid"),
            ("MMTestRecords3", "TestGuid"),
            ("MMAdditionalTR", "TestGuid"),
            ("TestVaccumPumpDetails", "TestGuid"),
            ("PulsationSystemResult", "TestGuid"),
            ("PulsationSystemResultRange", "TestGuid"),
            ("IndividualClusterAirflow", "TestGuid"),
            ("VisualFaultsMMStart", "TestGuid"),
            ("FaultInfo", "TestGuid"),
        };

        foreach (var (table, col) in satellites)
        {
            try
            {
                var total = await ScalarLong(conn, $"SELECT COUNT(*) FROM dbo.[{table}];");
                var nullKey = await ScalarLong(conn, $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{col}] IS NULL;");
                var orphans = await ScalarLong(conn, $@"
SELECT COUNT(*) FROM dbo.[{table}] s
WHERE s.[{col}] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Tests t WHERE t.[GUID] = s.[{col}]);");
                Console.WriteLine($"  {table,-28} rows={total,8:n0}  null-key={nullKey,5:n0}  guid-orphans={orphans,5:n0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {table,-28} (probe failed: {ex.Message})");
            }
        }
    }

    private static async Task IdentityAndBrandingProbe(SqlConnection conn)
    {
        Section("Identity & branding sources");
        try
        {
            Stat("Users (testers)", await ScalarLong(conn, "SELECT COUNT(*) FROM dbo.Users;"), "294");
            Stat("Companies", await ScalarLong(conn, "SELECT COUNT(*) FROM dbo.Companies;"), "84");

            try
            {
                var logos = await ScalarLong(conn,
                    "SELECT COUNT(*) FROM dbo.Companies WHERE ImagePath IS NOT NULL AND LEN(ImagePath) > 0;");
                Stat("Companies with a report logo", logos, "83 — migrated to TestingCompany.LogoData");
            }
            catch (Exception ex) { Warn(ex); }

            try
            {
                var certs = await ScalarLong(conn,
                    "SELECT COUNT(*) FROM dbo.Users WHERE CertificateNo IS NOT NULL AND LEN(CertificateNo) > 0;");
                Stat("Testers with a CertificateNo", certs, "migrated to Tester.CertificateNo");
            }
            catch (Exception ex) { Warn(ex); }

            Console.WriteLine("  UserType distribution (1=Tester, 2=CompanyAdministrator, 3=SuperAdministrator):");
            await using var cmd = new SqlCommand(
                "SELECT UserType, COUNT(*) AS n FROM dbo.Users GROUP BY UserType ORDER BY UserType;", conn)
            { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var type = r["UserType"] is DBNull ? "(null)" : r["UserType"].ToString();
                var label = type switch
                {
                    "1" => "Tester",
                    "2" => "CompanyAdministrator",
                    "3" => "SuperAdministrator",
                    _ => "(unmapped)"
                };
                Console.WriteLine($"      UserType {type,-3} {label,-22} {L(r, "n"),6:n0}");
            }
        }
        catch (Exception ex) { Warn(ex); }
    }

    // ---- helpers ----

    private static async Task<long> ScalarLong(SqlConnection conn, string sql)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 };
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? 0 : Convert.ToInt64(o);
    }

    private static long L(SqlDataReader r, string col) => r[col] is DBNull ? 0 : Convert.ToInt64(r[col]);

    private static string S(SqlDataReader r, string col) => r[col] is DBNull ? "(null)" : r[col].ToString() ?? "(null)";

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {title} " + new string('-', Math.Max(0, 66 - title.Length)));
    }

    private static void Stat(string label, object actual, string? expected = null)
    {
        var a = actual is long l ? l.ToString("n0") : actual.ToString() ?? "(null)";
        Console.WriteLine(expected is null
            ? $"  {label,-32} {a}"
            : $"  {label,-32} {a,-16} (expected {expected})");
    }

    private static void Warn(Exception ex) => Console.WriteLine($"  (probe failed: {ex.Message})");
}
