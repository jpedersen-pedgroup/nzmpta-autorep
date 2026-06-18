using System.Text;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Nzmpta.AutoRep.Migration.Mapping;
using Nzmpta.AutoRep.Migration.Source;
using Nzmpta.AutoRep.Migration.Util;

namespace Nzmpta.AutoRep.Migration.Pipeline;

/// <summary>
/// Migrates the legacy AutoRep DB into the new EF schema in FK order
/// (companies -> testers -> farms -> tests+config+payload). Idempotent: every target row is keyed
/// by a deterministic id derived from its legacy identity, and rows that already exist are skipped,
/// so a re-run converges the target rather than duplicating. All readings/faults land in
/// MachineTest.PayloadJson (the new schema has no reading child tables).
/// </summary>
public sealed class MigrationRunner
{
    public sealed record Options(string SourceConn, string TargetConn, string OutputDir, bool Cutover, int? Limit = null);

    private static readonly Guid Ns = new("8b1d3e90-5c2a-4f7b-9a3e-2d6c1f0a4b77");
    private static readonly DateTimeOffset Epoch = new(2011, 7, 18, 0, 0, 0, TimeSpan.FromHours(12));
    private static readonly TimeZoneInfo NzTz = ResolveNzTz();
    // Far-future lockout to carry a legacy account's deactivated state (mirrors the admin "deactivate").
    private static readonly DateTimeOffset LockedOutForever = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] SingleTables =
    {
        "MMTestSummary", "MMTestRecords1", "MMTestRecords2", "MMTestRecords3", "MMAdditionalTR",
        "VisualFaultsMMStart", "VisualFaultsMMRunning1", "VisualFaultsMMRunning2",
        "VisualFaultsMMRunning3", "VisualFaultsMMRunning4",
    };
    private static readonly string[] MultiTables =
    {
        "TestVaccumPumpDetails", "PulsationSystemResult", "PulsationSystemResultRange",
        "IndividualClusterAirflow", "FaultInfo",
    };
    private static readonly Dictionary<string, string> PayloadKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MMTestSummary"] = "summary", ["MMTestRecords1"] = "record1", ["MMTestRecords2"] = "record2",
        ["MMTestRecords3"] = "record3", ["MMAdditionalTR"] = "additional",
        ["VisualFaultsMMStart"] = "visualStart", ["VisualFaultsMMRunning1"] = "visualRunning1",
        ["VisualFaultsMMRunning2"] = "visualRunning2", ["VisualFaultsMMRunning3"] = "visualRunning3",
        ["VisualFaultsMMRunning4"] = "visualRunning4",
        ["TestVaccumPumpDetails"] = "vacuumPumps", ["PulsationSystemResult"] = "pulsationResults",
        ["PulsationSystemResultRange"] = "pulsationRanges", ["IndividualClusterAirflow"] = "clusterAirflow",
        ["FaultInfo"] = "faults",
    };
    private static readonly HashSet<string> NoiseCols =
        new(StringComparer.OrdinalIgnoreCase) { "TestGuid", "UserID", "CompanyID", "IsSynced" };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private sealed class LogicalTest
    {
        public Guid SourceGuid;
        public string TestNo = "";
        public Dictionary<string, object?> Header = null!;
        public Dictionary<string, object?>? Tdfi;
        public string? FarmKey;
        public Guid FarmId;
        public Guid ClientId;
    }

    private readonly Options _o;
    public MigrationRunner(Options o) => _o = o;

    private AutorepDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<AutorepDbContext>()
            .UseSqlServer(_o.TargetConn, sql => sql.CommandTimeout(180))
            .Options;
        var ctx = new AutorepDbContext(opts);
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;
        return ctx;
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_o.OutputDir);
        Console.WriteLine($"AutoRep migration — {(_o.Cutover ? "CUTOVER" : "dry-run")}");
        Console.WriteLine(new string('=', 70));

        using var src = new SqlConnection(_o.SourceConn);
        src.Open();
        Console.WriteLine($"Source: {src.DataSource}/{src.Database}");

        await using (var probe = NewContext())
        {
            var existing = await probe.MachineTests.CountAsync();
            Console.WriteLine($"Target: connected — {existing:n0} MachineTests already present.");
        }

        var q = new Quarantine();
        var ids = new IdMaps();
        var counts = new List<(string Entity, int Source, int Migrated, int Skipped)>();

        counts.Add(MigrateCompanies(src, q, ids));
        counts.Add(MigrateTesters(src, q, ids));
        var logicals = BuildLogicalTests(src, q);
        if (_o.Limit is { } lim && lim < logicals.Count)
        {
            Console.WriteLine($"  (--limit {lim}: processing the first {lim:n0} of {logicals.Count:n0} logical tests)");
            logicals = logicals.GetRange(0, lim);
        }
        counts.Add(MigrateFarms(logicals, q, ids));
        counts.Add(MigrateTests(src, logicals, q, ids));

        // --- reports ---
        var csvPath = Path.Combine(_o.OutputDir, "data-quality.csv");
        q.WriteCsv(csvPath);
        WriteSummary(Path.Combine(_o.OutputDir, "reconciliation.csv"), counts);

        Console.WriteLine();
        Console.WriteLine("-- Reconciliation " + new string('-', 52));
        Console.WriteLine($"  {"Entity",-22}{"Source",10}{"Migrated",12}{"Skipped",10}");
        foreach (var c in counts)
            Console.WriteLine($"  {c.Entity,-22}{c.Source,10:n0}{c.Migrated,12:n0}{c.Skipped,10:n0}");

        Console.WriteLine();
        Console.WriteLine($"-- Data quality ({q.Count:n0} rows flagged) " + new string('-', 30));
        foreach (var kv in q.ReasonCounts().OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Key,-38}{kv.Value,8:n0}");

        Console.WriteLine();
        Console.WriteLine($"Output: {csvPath}");
        Console.WriteLine("Done.");
        return 0;
    }

    // ---------------------------------------------------------------- Companies
    private (string, int, int, int) MigrateCompanies(SqlConnection src, Quarantine q, IdMaps ids)
    {
        var rows = Db.Query(src, "SELECT ID, CompanyName, IsActive, ImagePath FROM dbo.Companies");
        using var ctx = NewContext();
        var existingIds = ctx.TestingCompanies.Select(c => c.Id).ToHashSet();
        var usedNames = ctx.TestingCompanies.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var add = new List<TestingCompany>();
        int migrated = 0, skipped = 0;
        foreach (var r in rows)
        {
            var cid = Row.Int(r, "ID")!.Value;
            var id = DeterministicGuid.Create(Ns, $"company:{cid}");
            ids.Company[cid] = id;
            if (existingIds.Contains(id)) { skipped++; continue; }

            var name = Row.Str(r, "CompanyName", 450);
            if (name is null) { q.Add("Companies", cid.ToString(), "TestingCompany", "company_name_missing"); continue; }
            if (!usedNames.Add(name))
            {
                name = $"{name} (CompanyID {cid})";
                usedNames.Add(name);
                q.Add("Companies", cid.ToString(), "TestingCompany", "company_name_collision_suffixed", "info");
            }

            var tc = new TestingCompany { Id = id, Name = name, IsActive = Row.Truthy(r, "IsActive"), CreatedAt = Epoch };
            var img = Row.Str(r, "ImagePath");
            if (img is not null)
            {
                var (bytes, ct) = DecodeImage(img);
                if (bytes is not null) { tc.LogoData = bytes; tc.LogoContentType = ct; }
                else q.Add("Companies", cid.ToString(), "TestingCompany", "logo_decode_failed", "info");
            }
            add.Add(tc);
            migrated++;
        }
        ctx.TestingCompanies.AddRange(add);
        ctx.SaveChanges();
        return ("TestingCompanies", rows.Count, migrated, skipped);
    }

    // ---------------------------------------------------------------- Testers
    private (string, int, int, int) MigrateTesters(SqlConnection src, Quarantine q, IdMaps ids)
    {
        var rows = Db.Query(src,
            "SELECT ID, CompanyID, UserName, Email, PhoneNo, MobileNo, CertificateNo, ExpiryDate, UserType, IsActive FROM dbo.Users");
        var hasher = new PasswordHasher<Tester>();

        using var ctx = NewContext();
        var roleMap = EnsureRoles(ctx);
        var existingIds = ctx.Users.Select(u => u.Id).ToHashSet();
        var usedNames = ctx.Users.Select(u => u.NormalizedUserName!).Where(n => n != null).ToHashSet(StringComparer.Ordinal);

        var addUsers = new List<Tester>();
        var addRoles = new List<IdentityUserRole<string>>();
        int migrated = 0, skipped = 0;

        // synthetic "Legacy/Unknown Tester" to keep owner-orphan tests
        ids.SyntheticUnknownTesterId = DeterministicGuid.Create(Ns, "user:legacy-unknown").ToString();
        if (!existingIds.Contains(ids.SyntheticUnknownTesterId))
        {
            const string email = "legacy-unknown@migrated.local";
            var norm = email.ToUpperInvariant();
            if (usedNames.Add(norm))
            {
                var u = NewIdentityUser(ids.SyntheticUnknownTesterId, email, "Legacy/Unknown Tester", null, null, null, null, hasher);
                addUsers.Add(u);
                addRoles.Add(new IdentityUserRole<string> { UserId = u.Id, RoleId = roleMap[Roles.Tester] });
            }
        }

        foreach (var r in rows)
        {
            var uid = Row.Int(r, "ID")!.Value;
            var id = DeterministicGuid.Create(Ns, $"user:{uid}").ToString();
            if (existingIds.Contains(id)) { ids.User[uid] = id; skipped++; continue; }

            // Quarantined users are NOT inserted, so they must NOT enter the id map — otherwise a
            // test owned by one would resolve to a never-saved AspNetUsers id and fail the FK
            // (instead of falling back to the synthetic unknown tester).
            var email = Row.Str(r, "Email", 256);
            if (email is null) { q.Add("Users", uid.ToString(), "Tester", "tester_email_missing"); continue; }
            var norm = email.ToUpperInvariant();
            if (!usedNames.Add(norm)) { q.Add("Users", uid.ToString(), "Tester", "identity_username_collision"); continue; }

            Guid? companyId = null;
            var cid = Row.Int(r, "CompanyID");
            if (cid is not null && ids.Company.TryGetValue(cid.Value, out var mapped)) companyId = mapped;
            else if (cid is not null) q.Add("Users", uid.ToString(), "Tester", "tester_company_orphan", "info");

            var phone = Row.Str(r, "MobileNo", 50) ?? Row.Str(r, "PhoneNo", 50);
            var cert = Row.Str(r, "CertificateNo", 50);
            var licence = LicenceDate(Row.Date(r, "ExpiryDate"));

            var u = NewIdentityUser(id, email, Row.Str(r, "UserName") ?? email, companyId, licence, cert, phone, hasher);
            // Preserve the legacy deactivated state: login + refresh enforce deactivation via
            // LockoutEnd, so a migrated inactive account must carry it or it becomes usable after a reset.
            if (!Row.Truthy(r, "IsActive"))
            {
                u.LockoutEnd = LockedOutForever;
                q.Add("Users", uid.ToString(), "Tester", "tester_was_inactive_lockedout", "info");
            }

            ids.User[uid] = id; // map only users actually inserted (existing users were mapped above)
            addUsers.Add(u);
            migrated++;

            var role = (Row.Int(r, "UserType")) switch
            {
                2 => Roles.CompanyAdministrator,
                3 => Roles.SuperAdministrator,
                _ => Roles.Tester,
            };
            addRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = roleMap[role] });
        }

        ctx.Users.AddRange(addUsers);
        ctx.SaveChanges();
        ctx.UserRoles.AddRange(addRoles);
        ctx.SaveChanges();
        return ("Testers (AspNetUsers)", rows.Count, migrated, skipped);
    }

    private static Tester NewIdentityUser(string id, string email, string displayName, Guid? companyId,
        DateOnly? licence, string? cert, string? phone, PasswordHasher<Tester> hasher)
    {
        var norm = email.ToUpperInvariant();
        var u = new Tester
        {
            Id = id,
            UserName = email,
            NormalizedUserName = norm,
            Email = email,
            NormalizedEmail = norm,
            EmailConfirmed = true,
            DisplayName = displayName,
            TestingCompanyId = companyId,
            LicenceExpiryDate = licence,
            CertificateNo = cert,
            PhoneNumber = phone,
            ForcedPasswordResetRequired = true,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
        // Random throwaway password — never persisted in clear; login is blocked until reset anyway.
        u.PasswordHash = hasher.HashPassword(u, Guid.NewGuid().ToString("N") + "Aa1!");
        return u;
    }

    private static Dictionary<string, string> EnsureRoles(AutorepDbContext ctx)
    {
        var map = ctx.Roles.Where(r => r.Name != null).ToDictionary(r => r.Name!, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var name in Roles.All)
        {
            if (map.ContainsKey(name)) continue;
            var role = new IdentityRole(name) { NormalizedName = name.ToUpperInvariant() };
            ctx.Roles.Add(role);
            map[name] = role.Id;
            added = true;
        }
        if (added) ctx.SaveChanges();
        return map;
    }

    // ---------------------------------------------------------------- Logical tests
    private List<LogicalTest> BuildLogicalTests(SqlConnection src, Quarantine q)
    {
        var headers = Db.Query(src,
            "SELECT [GUID],[IDNUMBER],[TestNo],[TestDate],[SynDate],[UserID],[CompanyID],[TestType],[TesterName],[RegNo],[ExpiryDate],[TEmail],[TPhone],[IsDelete] " +
            "FROM dbo.Tests");

        // Soft-deleted tests are excluded by decision, but recorded in the data-quality output so the
        // audit accounts for every source row (rather than dropping them silently in the SQL filter).
        foreach (var h in headers.Where(h => Row.Truthy(h, "IsDelete")))
        {
            var key = h["GUID"] is Guid g ? g.ToString() : Row.Str(h, "IDNUMBER") ?? "?";
            q.Add("Tests", key, "MachineTest", "softdelete_excluded", "info");
        }
        var active = headers.Where(h => !Row.Truthy(h, "IsDelete")).ToList();

        var tdfi = new Dictionary<Guid, Dictionary<string, object?>>();
        foreach (var r in Db.Query(src, "SELECT * FROM dbo.TestDairyFarmInfo"))
        {
            if (!TryGuid(r, "TestGuid", out var g)) continue;
            if (!tdfi.TryGetValue(g, out var cur) || SortKey(r) > SortKey(cur)) tdfi[g] = r;
        }

        var logicals = new List<LogicalTest>();
        foreach (var byGuid in active.Where(h => h["GUID"] is Guid).GroupBy(h => (Guid)h["GUID"]!))
        {
            var subs = byGuid.GroupBy(h => (Row.Str(h, "TestNo") ?? "").ToUpperInvariant()).ToList();
            if (subs.Count > 1) q.Add("Tests", byGuid.Key.ToString(), "MachineTest", "dup_guid_distinct_tests_split", "info");
            foreach (var s in subs)
            {
                var canonical = s.OrderByDescending(h => Row.Int(h, "IDNUMBER") ?? 0).First();
                var extra = s.Count() - 1;
                if (extra > 0) q.Add("Tests", byGuid.Key.ToString(), "MachineTest", "dup_guid_reinsert_collapsed", "info");
                logicals.Add(new LogicalTest
                {
                    SourceGuid = byGuid.Key,
                    TestNo = Row.Str(canonical, "TestNo") ?? "",
                    Header = canonical,
                    Tdfi = tdfi.GetValueOrDefault(byGuid.Key),
                });
            }
        }
        return logicals;
    }

    // ---------------------------------------------------------------- Farms
    private (string, int, int, int) MigrateFarms(List<LogicalTest> logicals, Quarantine q, IdMaps ids)
    {
        var sample = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        var sampleDate = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var minDate = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var lt in logicals)
        {
            lt.FarmKey = FarmKey(lt.Tdfi);
            if (lt.FarmKey is null || lt.Tdfi is null) continue;
            var d = Row.Date(lt.Header, "TestDate") ?? Epoch.DateTime;
            if (!minDate.TryGetValue(lt.FarmKey, out var lo) || d < lo) minDate[lt.FarmKey] = d;
            if (!sampleDate.TryGetValue(lt.FarmKey, out var hi) || d >= hi) { sampleDate[lt.FarmKey] = d; sample[lt.FarmKey] = lt.Tdfi; }
        }

        using var ctx = NewContext();
        var existingIds = ctx.Farms.Select(f => f.Id).ToHashSet();
        var mscByName = ctx.MilkSupplyCompanies.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

        var add = new List<Farm>();
        var addedIds = new HashSet<Guid>();
        int migrated = 0, skipped = 0;

        foreach (var (key, tdfi) in sample)
        {
            var id = DeterministicGuid.Create(Ns, $"farm:{key}");
            ids.Farm[key] = id;
            if (existingIds.Contains(id) || !addedIds.Add(id)) { skipped++; continue; }
            add.Add(BuildFarm(id, tdfi, mscByName, NzDate(minDate.GetValueOrDefault(key)) ?? Epoch));
            migrated++;
        }

        // placeholder farms for tests with no farm info (FK is required)
        foreach (var lt in logicals.Where(l => l.FarmKey is null))
        {
            var id = DeterministicGuid.Create(Ns, $"farm:test:{lt.SourceGuid}");
            lt.FarmId = id;
            if (existingIds.Contains(id) || !addedIds.Add(id)) continue;
            add.Add(new Farm { Id = id, Name = $"Unknown Farm (Test {lt.SourceGuid})", IsActive = true, CreatedAt = Epoch });
            q.Add("Tests", lt.SourceGuid.ToString(), "Farm", "test_no_farminfo", "info");
            migrated++;
        }

        // resolve keyed tests to their farm id
        foreach (var lt in logicals.Where(l => l.FarmKey is not null))
            lt.FarmId = ids.Farm[lt.FarmKey!];

        foreach (var chunk in Chunk(add, 1000))
        {
            using var c = NewContext();
            c.Farms.AddRange(chunk);
            c.SaveChanges();
        }
        return ("Farms", logicals.Count, migrated, skipped);
    }

    private static Farm BuildFarm(Guid id, IReadOnlyDictionary<string, object?> t, IReadOnlyDictionary<string, Guid> msc, DateTimeOffset created)
    {
        var name = Row.Str(t, "FarmOwner", 200) ?? Row.Str(t, "Occupier", 200) ?? Row.Str(t, "FarmLocation", 200)
                   ?? (Row.Str(t, "SupplyNumber") is { } sn ? $"Supply {sn}" : null) ?? "Unknown Farm";
        var f = new Farm
        {
            Id = id,
            Name = name.Length <= 200 ? name : name[..200],
            SupplyNumber = Row.Str(t, "SupplyNumber", 50),
            AddressLine1 = Row.Str(t, "FarmLocation", 200),
            AddressLine2 = Row.Str(t, "PostalAddress", 200),
            FarmerName = Row.Str(t, "FarmOwner", 200),
            ContactPhone = Row.Str(t, "Mobile", 50) ?? Row.Str(t, "LandPhone", 50),
            ContactEmail = Row.Str(t, "Email", 256),
            IsActive = true,
            CreatedAt = created,
        };
        var dc = Row.Str(t, "DairyCompany");
        if (dc is not null && msc.TryGetValue(dc, out var mid)) f.MilkSupplyCompanyId = mid;
        var occ = Row.Str(t, "Occupier");
        if (occ is not null && !string.Equals(occ, Row.Str(t, "FarmOwner"), StringComparison.OrdinalIgnoreCase))
        {
            var note = $"Occupier/sharemilker: {occ}";
            f.Notes = note.Length <= 2000 ? note : note[..2000];
        }
        return f;
    }

    private static string? FarmKey(IReadOnlyDictionary<string, object?>? t)
    {
        if (t is null) return null;
        var supply = Row.Str(t, "SupplyNumber");
        if (supply is not null)
        {
            // Supply numbers are unique only WITHIN a dairy company, so two farms supplying different
            // processors can share one — key on (company, supply) to avoid merging them into one Farm.
            var company = Row.Str(t, "DairyCompany") ?? Row.Int(t, "DairyCompanyID")?.ToString() ?? "";
            return $"S:{company.ToUpperInvariant()}|{supply.ToUpperInvariant()}";
        }
        var owner = Row.Str(t, "FarmOwner");
        var loc = Row.Str(t, "FarmLocation");
        if (owner is not null || loc is not null) return $"O:{owner?.ToUpperInvariant()}|{loc?.ToUpperInvariant()}";
        return null;
    }

    // ---------------------------------------------------------------- Tests (+ config + payload)
    private (string, int, int, int) MigrateTests(SqlConnection src, List<LogicalTest> logicals, Quarantine q, IdMaps ids)
    {
        using (var ctx0 = NewContext())
        {
            var existingClientIds = ctx0.MachineTests.Where(t => t.ClientId != null).Select(t => t.ClientId!.Value).ToHashSet();
            foreach (var lt in logicals) lt.ClientId = DeterministicGuid.Create(Ns, $"test:{lt.SourceGuid}|{lt.TestNo}");
            _pending = logicals.Where(lt => !existingClientIds.Contains(lt.ClientId)).ToList();
            _seen = existingClientIds;
        }

        int migrated = 0, skipped = logicals.Count - _pending.Count;
        const int batchSize = 250;
        for (var i = 0; i < _pending.Count; i += batchSize)
        {
            var batch = _pending.GetRange(i, Math.Min(batchSize, _pending.Count - i));
            var guids = batch.Select(b => b.SourceGuid).Distinct().ToList();

            var singles = SingleTables.ToDictionary(t => t, t => LoadSingleBatch(src, t, guids), StringComparer.OrdinalIgnoreCase);
            var multis = MultiTables.ToDictionary(t => t, t => LoadMultiBatch(src, t, guids), StringComparer.OrdinalIgnoreCase);

            using var ctx = NewContext();
            var add = new List<MachineTest>(batch.Count);
            foreach (var lt in batch)
            {
                if (!_seen.Add(lt.ClientId)) continue;
                var testDate = Row.Date(lt.Header, "TestDate");
                var mt = new MachineTest
                {
                    Id = Guid.NewGuid(),
                    ClientId = lt.ClientId,
                    TesterId = ResolveTester(lt, ids, q),
                    FarmId = lt.FarmId,
                    CreatedAt = NzDate(testDate) ?? Epoch,
                    MarkedCompleteAt = NzDate(Row.Date(lt.Header, "SynDate")) ?? NzDate(testDate),
                    PayloadJson = BuildPayload(lt, singles, multis),
                    Configuration = BuildConfig(lt, q),
                };
                add.Add(mt);
                migrated++;
            }
            ctx.MachineTests.AddRange(add);
            ctx.SaveChanges();
            Console.Write($"\r  tests: {Math.Min(i + batchSize, _pending.Count):n0}/{_pending.Count:n0} migrated...");
        }
        if (_pending.Count > 0) Console.WriteLine();
        return ("MachineTests", logicals.Count, migrated, skipped);
    }

    private List<LogicalTest> _pending = new();
    private HashSet<Guid> _seen = new();

    private string ResolveTester(LogicalTest lt, IdMaps ids, Quarantine q)
    {
        var uid = Row.Int(lt.Header, "UserID");
        if (uid is not null && ids.User.TryGetValue(uid.Value, out var tid)) return tid;
        q.Add("Tests", lt.SourceGuid.ToString(), "MachineTest", "test_owner_unresolved");
        return ids.SyntheticUnknownTesterId;
    }

    private static MachineConfiguration BuildConfig(LogicalTest lt, Quarantine q)
    {
        var t = lt.Tdfi;
        if (t is null)
        {
            q.Add("Tests", lt.SourceGuid.ToString(), "MachineConfiguration", "config_defaulted_no_tdfi", "info");
            return new MachineConfiguration { PlantType = PlantType.HerringboneLowline, ClusterCount = 0 };
        }
        var cfg = new MachineConfiguration
        {
            PlantType = Row.Int(t, "PlantType") switch
            {
                1 => PlantType.HerringboneHighline,
                2 => PlantType.HerringboneLowline,
                3 => PlantType.Rotary,
                _ => PlantType.Other,
            },
            PlantSize = Row.Str(t, "PlantSize"),
            ClusterCount = Row.Int(t, "PlantSize") ?? 0,
            FlushingPulsationSystem = Row.Truthy(t, "FlushingPulsationSystem"),
            PulsatorBrand = Row.Str(t, "PulsatorBrand"),
            PulsatorConfiguration = Row.Str(t, "PulsatorSize"),
            PulsatorCount = Row.Int(t, "NumberPulsator") ?? 0,
            ClawModel = Row.Str(t, "Claw", 150),
            ShellModel = Row.Str(t, "Shell", 150),
            LinerModel = Row.Str(t, "Liner", 150),
            BackLiner = Row.Str(t, "BackLiner"),
            LinerVented = Row.Truthy(t, "LinerVented"),
            NumberOfVacuumPumps = Row.Int(t, "NoofVacuumSystem") ?? 1,
            MilklineSize = Row.Str(t, "MilklineSize", 50),
            LastBmcc = Row.Str(t, "LastBMCC", 100),
            AtmosPressureSeaLevel = Row.Int(t, "AtmosPressureSeaLevel"),
            PumpLubrication = PumpLubrication.OilLubricated,
            VsdFitted = false,
            IsoPortsAvailable = true,
            HasAcr = Row.Truthy(t, "ACR"),
            HasBailGates = Row.Truthy(t, "BailGates"),
            HasMilkMeters = Row.Truthy(t, "MilkMeters"),
            HasTeatSprayer = Row.Truthy(t, "TeatSprayerVacuumSys"),
            HasBackingGate = Row.Truthy(t, "BackingGateVacuumSys"),
        };
        if (cfg.ClusterCount == 0) q.Add("Tests", lt.SourceGuid.ToString(), "MachineConfiguration", "clustercount_zero", "info");
        return cfg;
    }

    private static string BuildPayload(LogicalTest lt,
        Dictionary<string, Dictionary<Guid, Dictionary<string, object?>>> singles,
        Dictionary<string, Dictionary<Guid, List<Dictionary<string, object?>>>> multis)
    {
        var h = lt.Header;
        var p = new Dictionary<string, object?>
        {
            ["legacy"] = new Dictionary<string, object?>
            {
                ["guid"] = lt.SourceGuid, ["idNumber"] = Row.Int(h, "IDNUMBER"), ["testNo"] = lt.TestNo,
                ["testType"] = Row.Int(h, "TestType"), ["testDate"] = Row.Date(h, "TestDate"), ["synDate"] = Row.Date(h, "SynDate"),
            },
            ["signOff"] = new Dictionary<string, object?>
            {
                ["testerName"] = Row.Str(h, "TesterName"), ["regNo"] = Row.Str(h, "RegNo"), ["expiry"] = Row.Date(h, "ExpiryDate"),
            },
        };
        if (lt.Tdfi is not null) p["farmInfo"] = Clean(lt.Tdfi);

        foreach (var (table, byGuid) in singles)
            if (byGuid.TryGetValue(lt.SourceGuid, out var row)) p[PayloadKey[table]] = Clean(row);

        foreach (var (table, byGuid) in multis)
            if (byGuid.TryGetValue(lt.SourceGuid, out var list) && list.Count > 0)
                p[PayloadKey[table]] = list.Select(Clean).ToList();

        return JsonSerializer.Serialize(p, Json);
    }

    // ---------------------------------------------------------------- satellite loaders
    private static Dictionary<Guid, Dictionary<string, object?>> LoadSingleBatch(SqlConnection src, string table, List<Guid> guids)
    {
        var byGuid = new Dictionary<Guid, Dictionary<string, object?>>();
        if (guids.Count == 0) return byGuid;
        try
        {
            foreach (var r in Db.Query(src, $"SELECT * FROM dbo.[{table}] WHERE [TestGuid] IN ({Db.GuidInList(guids)})"))
            {
                if (!TryGuid(r, "TestGuid", out var g)) continue;
                if (!byGuid.TryGetValue(g, out var cur) || SortKey(r) > SortKey(cur)) byGuid[g] = r;
            }
        }
        catch { /* table/column variance — payload simply omits this section */ }
        return byGuid;
    }

    private static Dictionary<Guid, List<Dictionary<string, object?>>> LoadMultiBatch(SqlConnection src, string table, List<Guid> guids)
    {
        var byGuid = new Dictionary<Guid, List<Dictionary<string, object?>>>();
        if (guids.Count == 0) return byGuid;
        try
        {
            foreach (var r in Db.Query(src, $"SELECT * FROM dbo.[{table}] WHERE [TestGuid] IN ({Db.GuidInList(guids)})"))
            {
                if (!TryGuid(r, "TestGuid", out var g)) continue;
                if (!byGuid.TryGetValue(g, out var list)) byGuid[g] = list = new List<Dictionary<string, object?>>();
                list.Add(r);
            }
        }
        catch { /* tolerate variance */ }
        return byGuid;
    }

    // ---------------------------------------------------------------- helpers
    private static Dictionary<string, object?> Clean(IReadOnlyDictionary<string, object?> r)
    {
        var d = new Dictionary<string, object?>(r.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in r)
        {
            if (kv.Value is null || NoiseCols.Contains(kv.Key)) continue;
            d[kv.Key] = kv.Value;
        }
        return d;
    }

    private static long SortKey(IReadOnlyDictionary<string, object?> r) =>
        ((long)(Row.Int(r, "IDNUMBER") ?? 0) << 32) | (uint)(Row.Int(r, "ID") ?? 0);

    private static bool TryGuid(IReadOnlyDictionary<string, object?> r, string col, out Guid g)
    {
        if (r.TryGetValue(col, out var v) && v is Guid gg) { g = gg; return true; }
        g = default;
        return false;
    }

    private static DateOnly? LicenceDate(DateTime? d)
    {
        if (d is null) return null;
        if (d.Value.Year <= 1901 || d.Value.Year >= 2030) return null; // 1900/2030 sentinels
        return DateOnly.FromDateTime(d.Value);
    }

    private static DateTimeOffset? NzDate(DateTime? d)
    {
        if (d is null) return null;
        var local = DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, NzTz.GetUtcOffset(local));
    }

    private static (byte[]? bytes, string? contentType) DecodeImage(string s)
    {
        try
        {
            string ct = "image/png", b64 = s;
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = s.IndexOf(',');
                if (comma > 0)
                {
                    var header = s[5..comma];
                    var semi = header.IndexOf(';');
                    ct = semi > 0 ? header[..semi] : header;
                    b64 = s[(comma + 1)..];
                }
            }
            return (Convert.FromBase64String(b64.Trim()), ct);
        }
        catch { return (null, null); }
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }

    private static void WriteSummary(string path, List<(string Entity, int Source, int Migrated, int Skipped)> counts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Entity,SourceRows,Migrated,SkippedExisting");
        foreach (var c in counts) sb.AppendLine($"{c.Entity},{c.Source},{c.Migrated},{c.Skipped}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static TimeZoneInfo ResolveNzTz()
    {
        foreach (var id in new[] { "New Zealand Standard Time", "Pacific/Auckland" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
        }
        return TimeZoneInfo.Utc;
    }
}
