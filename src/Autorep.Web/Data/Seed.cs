using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Data;

public static class Seed
{
    // Ensures the three application roles exist. Safe to call repeatedly.
    public static async Task RolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    // Seeds the reference-data lookups (NZ regions, dairy processors). Idempotent
    // (insert-if-absent by name) and run in every environment. NZMPTA can add or
    // deactivate rows later via the admin portal without conflicting with this.
    public static async Task ReferenceDataAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AutorepDbContext>();

        // 16 official NZ regions, north to south, grouped by island.
        (string Name, string Island)[] regions =
        [
            ("Northland", "North Island"), ("Auckland", "North Island"),
            ("Waikato", "North Island"), ("Bay of Plenty", "North Island"),
            ("Gisborne", "North Island"), ("Hawke's Bay", "North Island"),
            ("Taranaki", "North Island"), ("Manawatū-Whanganui", "North Island"),
            ("Wellington", "North Island"),
            ("Tasman", "South Island"), ("Nelson", "South Island"),
            ("Marlborough", "South Island"), ("West Coast", "South Island"),
            ("Canterbury", "South Island"), ("Otago", "South Island"),
            ("Southland", "South Island"),
        ];
        for (var i = 0; i < regions.Length; i++)
        {
            var (name, island) = regions[i];
            var existing = await db.Regions.FirstOrDefaultAsync(r => r.Name == name);
            if (existing is null)
            {
                db.Regions.Add(new Region { Name = name, Island = island, SortOrder = i + 1 });
            }
            else if (string.IsNullOrEmpty(existing.Island))
            {
                existing.Island = island; // backfill island on pre-split rows
            }
        }

        // Main NZ dairy processors — seeded once as defaults, then NZMPTA owns the
        // list via the admin portal. Seed only when the table is empty so admin
        // renames/deletes aren't resurrected on the next startup. (Regions above are
        // a fixed national list, so they stay always-ensured.)
        if (!await db.MilkSupplyCompanies.AnyAsync())
        {
            string[] processors =
            [
                "Fonterra", "Open Country Dairy", "Synlait", "Westland Milk Products",
                "Tatua", "Miraka", "Oceania Dairy", "Mataura Valley Milk",
                "Green Valley Dairies", "Goodman Fielder",
            ];
            foreach (var name in processors)
            {
                db.MilkSupplyCompanies.Add(new MilkSupplyCompany { Name = name });
            }
        }

        await db.SaveChangesAsync();
        await TestStandardsAsync(db);
        await EquipmentAsync(db);
    }

    // Seeds the equipment catalogs (shells / liners / pulsator models from the legacy DB, embedded
    // as JSON resources, plus the small fixed lists). Seed-if-type-empty so SuperAdmin renames,
    // deactivations and deletions are never resurrected on restart.
    private static async Task EquipmentAsync(AutorepDbContext db)
    {
        static string[] Strings(string resource)
        {
            using var stream = typeof(Seed).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing embedded resource {resource}");
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(stream) ?? [];
        }

        var hasType = await db.EquipmentItems.Select(e => e.Type).Distinct().ToListAsync();

        if (!hasType.Contains(EquipmentItem.Shell))
        {
            foreach (var name in Strings("Autorep.Web.Data.SeedData.shells.json"))
                db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.Shell, Name = name });
        }
        if (!hasType.Contains(EquipmentItem.Liner))
        {
            foreach (var name in Strings("Autorep.Web.Data.SeedData.liners.json"))
                db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.Liner, Name = name });
        }
        if (!hasType.Contains(EquipmentItem.Pulsator))
        {
            using var stream = typeof(Seed).Assembly.GetManifestResourceStream("Autorep.Web.Data.SeedData.pulsators.json")
                ?? throw new InvalidOperationException("Missing embedded resource pulsators.json");
            var models = System.Text.Json.JsonSerializer.Deserialize<List<PulsatorSeed>>(stream,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            foreach (var m in models)
                db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.Pulsator, Name = m.Name, Brand = m.Brand });
        }
        if (!hasType.Contains(EquipmentItem.MilklineSize))
        {
            foreach (var name in new[] { "50", "63", "75", "100" })
                db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.MilklineSize, Name = name });
        }
        if (!hasType.Contains(EquipmentItem.PulsatorConfiguration))
        {
            foreach (var name in new[] { "2 X 2", "4 + 0" })
                db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.PulsatorConfiguration, Name = name });
        }

        await db.SaveChangesAsync();
    }

    private sealed record PulsatorSeed(string Name, string Brand);

    // Seeds the editable test standards (pass/fail thresholds + formula parameters) with the
    // values verified against the NZMPTA Testing Standards Manual + ISO 6690:2007 (see
    // plans/reference/standards-audit.md). Insert-if-absent BY KEY so SuperAdmin edits are never
    // overwritten on restart, while new keys added in later releases still appear.
    private static async Task TestStandardsAsync(AutorepDbContext db)
    {
        TestStandard Rule(string key, string label, string category, string kind, string? unit,
            string? source, double? limit = null, double? min = null, double? max = null,
            double? target = null, double? tolerance = null) => new()
        {
            Key = key, Label = label, Category = category, Kind = kind, Unit = unit,
            SourceRef = source, Limit = limit, Min = min, Max = max, Target = target, Tolerance = tolerance,
        };
        TestStandard Param(string key, string label, string category, double value, string? unit, string? source) => new()
        {
            Key = key, Label = label, Category = category, Kind = "param", Value = value, Unit = unit, SourceRef = source,
        };

        TestStandard[] standards =
        [
            // — Vacuum system (Test Record) —
            Rule("tr.workingVacuum", "Working vacuum @ receiver — maximum", "Vacuum system", "atMost", "kPa", "Manual p40", limit: 50),
            Rule("tr.regulationDeviation", "Vacuum regulation deviation", "Vacuum system", "tolerance", "kPa", "Manual p40 / ISO D.2.7", target: 0, tolerance: 2),
            Rule("tr.fallOff", "Fall-off vacuum drop", "Vacuum system", "atMost", "kPa", "Manual p40 / ISO D.1.14", limit: 2),
            Rule("tr.regulationUndershoot", "Regulation undershoot", "Vacuum system", "atMost", "kPa", "Manual p40 / ISO D.1.7", limit: 2),
            Rule("tr.regulationOvershoot", "Regulation overshoot", "Vacuum system", "atMost", "kPa", "Manual p40 / ISO D.1.8", limit: 2),
            Rule("tr.airlineDropRR", "Vacuum drop receiver → regulator", "Vacuum system", "atMost", "kPa", "Manual p40 / ISO D.2.13", limit: 1),
            Rule("tr.airlinePumpDrop", "Vacuum drop receiver → pump", "Vacuum system", "atMost", "kPa", "Manual p40/p44 / ISO D.2.15", limit: 3),
            Rule("tr.regulatorSensitivity", "Regulator sensitivity", "Vacuum system", "atMost", "kPa", "Manual p40 / ISO D.2.6", limit: 1),
            Rule("tr.gaugeError", "Farm gauge error vs test gauge", "Vacuum system", "tolerance", "kPa", "Manual p40 / ISO D.2.3", target: 0, tolerance: 1),
            Param("param.vsd.maxRise", "Max vacuum rise @ minimum VSD speed", "Vacuum system", 2, "kPa", "Manual p40"),

            // — Reserve —
            Param("param.reserve.lossPct", "Regulation loss — % of manual reserve", "Reserve", 10, "%", "Manual p40 / ISO C.4.6"),
            Param("param.reserve.lossFloor", "Regulation loss — minimum allowance", "Reserve", 35, "L/min", "Manual p40 / ISO C.4.6"),
            Param("param.reserve.leakPct", "Regulator leakage — % of manual reserve", "Reserve", 5, "%", "Manual p39/41 / ISO C.4.8"),
            Param("param.reserve.leakFloor", "Regulator leakage — minimum allowance", "Reserve", 35, "L/min", "Manual p39/41 / ISO C.4.8"),

            // — Leakage —
            Param("param.vacLeak.pctOfPumpCapacity", "Vacuum system leakage — % of pump capacity", "Leakage", 5, "%", "Manual p41 / ISO C.5.4"),
            Param("param.milkLeak.base", "Milk system leakage — base allowance", "Leakage", 10, "L/min", "Manual p41 / ISO C.5.6"),
            Param("param.milkLeak.perCluster", "Milk system leakage — per cluster", "Leakage", 2, "L/min", "Manual p41 / ISO C.5.6"),

            // — Ancillary allowances —
            Param("param.ancillary.perUnit", "ACR / milk-meter allowance per unit", "Ancillary", 7.5, "L/min", "Manual p41"),
            Param("param.ancillary.minTotal", "ACR / milk-meter minimum total allowance", "Ancillary", 30, "L/min", "Manual p41"),
            Param("param.perCluster.tenLpm", "Teat spray / vacuum-gate allowance per cluster", "Ancillary", 10, "L/min", "Manual p41"),
            Param("param.pulsator.consumptionPer10", "Pulsator consumption per 10 units", "Ancillary", 30, "L/min", "Manual p41"),
            Rule("add.regulatorLoad", "Peak regulator load — max increase", "Ancillary", "atMost", "kPa", "Manual p61", limit: 2),

            // — Cluster air —
            Rule("add.clusterAirAdmission", "Cluster air admission per cluster", "Cluster air", "between", "L/min", "Manual p42 / ISO D.6", min: 4, max: 12),
            Param("param.clusterAir.ventedMax", "Cluster air admission max — vented liners", "Cluster air", 35, "L/min", "Manual pp41–42"),
            Rule("ica.totalAirAdmission", "Individual cluster — total air admission max", "Cluster air", "atMost", "L/min", "ISO Table D.6", limit: 12),
            Rule("ica.leakage", "Individual cluster — leakage max", "Cluster air", "atMost", "L/min", "ISO Table D.6", limit: 2),
            Rule("ica.airVentAdmission", "Individual cluster — air-vent admission min", "Cluster air", "atLeast", "L/min", "ISO Table D.6", min: 4),

            // — Pulsation —
            Param("param.pulsation.rateSpreadMax", "Rate spread between pulsators — max", "Pulsation", 6, "ppm", "Manual p49"),
            Param("param.pulsation.ratioSpreadMax", "Ratio variation between pulsators — max", "Pulsation", 5, "%", "Manual p49"),
            Param("param.pulsation.limpMax", "Limping within a cluster — max", "Pulsation", 5, "%", "Manual p49 / ISO D.5"),
            Rule("puls.row.phaseB", "Pulsation phase b — minimum", "Pulsation", "atLeast", "%", "ISO Table D.5", min: 30),
            Rule("puls.row.phaseDms", "Pulsation phase d — minimum duration", "Pulsation", "atLeast", "ms", "ISO Table D.5 / Manual p68", min: 150),
            Param("param.chamberVac.maxDelta", "Max chamber vacuum — max drop below working vacuum", "Pulsation", 2, "kPa", "Manual p40 / ISO D.2.17"),
            Rule("puls.airlineStability", "Pulsator airline vacuum dips — max", "Pulsation", "atMost", "kPa", "Manual p40", limit: 4),
        ];

        var existingKeys = await db.TestStandards.Select(s => s.Key).ToListAsync();
        foreach (var std in standards.Where(s => !existingKeys.Contains(s.Key)))
        {
            db.TestStandards.Add(std);
        }
        await db.SaveChangesAsync();
    }

    // Creates the initial NZMPTA Super-Administrator from configuration, when no account with
    // that email exists yet. No password lives in source: set Bootstrap:AdminEmail and
    // Bootstrap:AdminPassword (the password via Key Vault / an App Service secret). The
    // account is flagged for a forced password change on first login, so the bootstrap
    // password is single-use. Safe to leave configured — it's a no-op once the user exists.
    public static async Task BootstrapAdminAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return; // not configured — no insecure default

        var userManager = services.GetRequiredService<UserManager<Tester>>();
        if (await userManager.FindByEmailAsync(email) is not null)
            return; // never overwrite or reset an existing account

        var user = new Tester
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "NZMPTA Administrator",
            ForcedPasswordResetRequired = true,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap")
                .LogError("Failed to create bootstrap admin {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(user, Roles.SuperAdministrator);
    }

    // Development-only: creates a default Super-Administrator and Tester
    // so you can sign in immediately after first run. NEVER call this in
    // staging or prod.
    public static async Task DevUsersAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<Tester>>();

        await EnsureUser(userManager, "admin@local",  "DevPassword123!", "Local Super Admin", Roles.SuperAdministrator);
        await EnsureUser(userManager, "tester@local", "DevPassword123!", "Local Tester",      Roles.Tester);
    }

    private static async Task EnsureUser(
        UserManager<Tester> userManager,
        string email,
        string password,
        string displayName,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new Tester
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed dev user {email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
