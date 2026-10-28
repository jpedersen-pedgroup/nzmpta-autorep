using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.Admin.Equipment;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The OEM Specs: the pulsator and vacuum pump specs the SuperAdmin maintains as dated versions,
// seeded once from the specs the device bundled, served to devices with each item's id and spec
// history, and edited on the admin pages.
public class OemSpecsTests
{
    private const string Bundled = "Electronic (Simultaneous)"; // Dairymaster: rate 59–61, ratio 59–69
    private static DateOnly Today => NzTime.Today;

    // A database of its own per test, with the services the reference seed needs.
    private static ServiceProvider NewServices()
    {
        var name = "oemspecs-" + Guid.NewGuid();
        return new ServiceCollection()
            .AddLogging()
            .AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(name))
            .BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await Seed.ReferenceDataAsync(scope.ServiceProvider);
    }

    private static AutorepDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AutorepDbContext>();

    private static IQueryable<EquipmentItem> Items(AutorepDbContext db) => db.EquipmentItems.Include(e => e.SpecVersions);

    private static EquipmentSpecVersion FromTheStart(object spec) => new() { SpecJson = OemSpecs.Serialize(spec) };

    [Fact]
    public async Task Seed_gives_every_pulsator_model_and_vacuum_pump_its_bundled_spec_from_the_start()
    {
        using var sp = NewServices();
        await SeedAsync(sp);

        using var scope = sp.CreateScope();
        var db = Db(scope);
        var pulsators = await Items(db).Where(e => e.Type == EquipmentItem.Pulsator).ToListAsync();
        var pumps = await Items(db).Where(e => e.Type == EquipmentItem.VacuumPump).ToListAsync();
        pulsators.Should().HaveCount(127).And.OnlyContain(e => e.IsActive && e.SpecVersions.Count == 1);
        pumps.Should().HaveCount(140).And.OnlyContain(e => e.IsActive && e.SpecVersions.Count == 1);
        (await db.EquipmentSpecVersions.AnyAsync(v => v.EffectiveFrom != null)).Should().BeFalse();

        OemSpecs.Pulsator(pulsators.Single(e => e.Name == Bundled), Today).Should().BeEquivalentTo(new PulsatorSpec
        {
            RateMin = 59, RateMax = 61, RatioMin = 59, RatioMax = 69, PhaseB = 40, PhaseD = 20,
            MaxChamberVacuum = 10, RecommendedRate = 60, RecommendedRatio = 60, MaxClustersPerPulsator = "2",
        });
        OemSpecs.VacuumPump(pumps.Single(e => e.Brand == "De Laval" && e.Name == "DVP1600"), Today)
            .Should().BeEquivalentTo(new VacuumPumpSpec { MinRpm = 275, MaxRpm = 1400, AirFlow = 1.15, MotorSizeFactor = 35 });

        // Only pulsators and vacuum pumps carry specs.
        (await db.EquipmentSpecVersions.CountAsync()).Should().Be(127 + 140);
        (await db.AppliedSeeds.AnyAsync(s => s.Key == Seed.OemSpecsSeedKey)).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_matches_a_database_that_already_has_items_and_keeps_what_admins_changed()
    {
        // Staging before this release: the catalogs were seeded from the same lists and then edited.
        using var sp = NewServices();
        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            db.EquipmentItems.AddRange(
                // The same model with its spacing and case changed: still the same model.
                new EquipmentItem { Type = EquipmentItem.Pulsator, Name = "electronic  (simultaneous)", Brand = "Dairymaster" },
                // A model an admin renamed: nothing in the bundled specs has this name.
                new EquipmentItem { Type = EquipmentItem.Pulsator, Name = "EPIC 60/65 (renamed)", Brand = "Delaval" },
                // A pump that somehow has a spec already keeps it.
                new EquipmentItem
                {
                    Type = EquipmentItem.VacuumPump, Name = "DVP1600", Brand = "DE LAVAL",
                    SpecVersions = [FromTheStart(new VacuumPumpSpec { AirFlow = 9 })],
                });
            await db.SaveChangesAsync();
        }

        await SeedAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            var items = await Items(db).Where(e => OemSpecs.SpecTypes.Contains(e.Type)).ToListAsync();
            OemSpecs.Pulsator(items.Single(e => e.Name == "electronic  (simultaneous)"), Today)!.RateMax.Should().Be(61);
            items.Single(e => e.Name == "EPIC 60/65 (renamed)").SpecVersions.Should().BeEmpty();
            var pump = items.Single(e => e.Brand == "DE LAVAL");
            pump.SpecVersions.Should().ContainSingle();
            OemSpecs.VacuumPump(pump, Today)!.AirFlow.Should().Be(9);

            // The bundled rows that matched nothing are kept, with their specs, as deactivated items:
            // in the admin's view, out of testers' dropdowns.
            var added = items.Where(e => !e.IsActive).ToList();
            added.Should().HaveCount(126 + 139).And.OnlyContain(e => e.SpecVersions.Count == 1);
            added.Should().Contain(e => e.Name == "DeLaval EP 100/100 B 1 cluster EPIC (60/65)" && e.Brand == "Delaval");
            added.Should().NotContain(e => e.Name == Bundled);
            items.Count(e => e.Type == EquipmentItem.Pulsator).Should().Be(2 + 126);
        }
    }

    [Fact]
    public async Task Seed_runs_once_so_a_later_edit_clear_or_delete_sticks()
    {
        using var sp = NewServices();
        await SeedAsync(sp);
        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            var pump = await Items(db).SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP1600");
            pump.SpecVersions.Single().SpecJson = OemSpecs.Serialize(new VacuumPumpSpec { AirFlow = 2 });
            db.EquipmentSpecVersions.RemoveRange((await Items(db).SingleAsync(e => e.Name == Bundled)).SpecVersions);
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP2300"));
            await db.SaveChangesAsync();
        }

        await SeedAsync(sp);
        await SeedAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            var pump = await Items(db).SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP1600");
            OemSpecs.VacuumPump(pump, Today)!.AirFlow.Should().Be(2);
            (await Items(db).SingleAsync(e => e.Name == Bundled)).SpecVersions.Should().BeEmpty();
            (await db.EquipmentItems.AnyAsync(e => e.Name == "DVP2300")).Should().BeFalse();
            (await db.EquipmentItems.CountAsync(e => e.Type == EquipmentItem.Pulsator)).Should().Be(127);
        }
    }

    [Fact]
    public async Task Devices_get_each_item_with_its_id_and_spec_history_and_deactivated_models_for_their_specs()
    {
        using var factory = new AuthedWebAppFactory();
        Guid deactivated;
        var ahead = Today.AddDays(30);
        using (var scope = factory.Services.CreateScope())
        {
            await Seed.ReferenceDataAsync(scope.ServiceProvider);
            var db = Db(scope);
            var item = await Items(db).SingleAsync(e => e.Name == Bundled);
            item.IsActive = false;
            deactivated = item.Id;
            var dvp = await Items(db).SingleAsync(e => e.Brand == "De Laval" && e.Name == "DVP1600");
            db.EquipmentSpecVersions.Add(new EquipmentSpecVersion
            {
                EquipmentItemId = dvp.Id, EffectiveFrom = ahead, SpecJson = OemSpecs.Serialize(new VacuumPumpSpec { AirFlow = 1.2 }),
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClientAs(Roles.Tester, "tester-oemspecs-1");
        var res = await client.GetFromJsonAsync<JsonElement>("/api/equipment");
        var items = res.GetProperty("items").EnumerateArray().ToList();
        var inactive = res.GetProperty("inactive").EnumerateArray().ToList();

        var pump = items.Single(i => i.GetProperty("brand").GetString() == "De Laval" && i.GetProperty("name").GetString() == "DVP1600");
        pump.GetProperty("id").GetGuid().Should().NotBeEmpty();
        // `spec` is today's, for devices from before effective dates; `specs` the whole history.
        pump.GetProperty("spec").GetProperty("airFlow").GetDouble().Should().Be(1.15);
        var specs = pump.GetProperty("specs").EnumerateArray().ToList();
        specs.Should().HaveCount(2);
        specs[0].GetProperty("effectiveFrom").ValueKind.Should().Be(JsonValueKind.Null);
        specs[0].GetProperty("spec").GetProperty("airFlow").GetDouble().Should().Be(1.15);
        specs[1].GetProperty("effectiveFrom").GetString().Should().Be(ahead.ToString("yyyy-MM-dd"));
        specs[1].GetProperty("spec").GetProperty("airFlow").GetDouble().Should().Be(1.2);

        // A type without specs still says so, so a device can tell "no spec" from an older server.
        var shell = items.First(i => i.GetProperty("type").GetString() == "Shell");
        shell.GetProperty("spec").ValueKind.Should().Be(JsonValueKind.Null);
        shell.GetProperty("specs").GetArrayLength().Should().Be(0);

        items.Should().NotContain(i => i.GetProperty("id").GetGuid() == deactivated);
        var kept = inactive.Single(i => i.GetProperty("id").GetGuid() == deactivated);
        kept.GetProperty("spec").GetProperty("rateMin").GetDouble().Should().Be(59);
    }

    [Fact]
    public async Task A_model_added_with_its_spec_is_served_to_devices_from_its_date()
    {
        using var factory = new AuthedWebAppFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var page = new NewModel(Db(scope))
            {
                Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo",
                Spec = new SpecInput { RateMin = 58, RateMax = 62, RatioMin = 60, RatioMax = 70, MaxClustersPerPulsator = " 2 ", EffectiveFrom = Today },
                PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            };
            (await page.OnPostAsync()).Should().BeOfType<RedirectToPageResult>();
            page.Errors.Should().BeEmpty();
        }

        var client = factory.CreateClientAs(Roles.Tester, "tester-oemspecs-2");
        var res = await client.GetFromJsonAsync<JsonElement>("/api/equipment");
        var item = res.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "NewCo P1");
        var spec = item.GetProperty("spec");
        spec.GetProperty("rateMin").GetDouble().Should().Be(58);
        spec.GetProperty("ratioMax").GetDouble().Should().Be(70);
        spec.GetProperty("maxClustersPerPulsator").GetString().Should().Be("2");
        spec.GetProperty("phaseB").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("specs")[0].GetProperty("effectiveFrom").GetString().Should().Be(Today.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Renaming_an_item_keeps_its_spec_and_a_spec_change_is_a_new_audited_version()
    {
        using var factory = new AuthedWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var item = new EquipmentItem
        {
            Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo",
            SpecVersions = [FromTheStart(new PulsatorSpec { RateMin = 58, RateMax = 62 })],
        };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();

        // The form posts the spec back as it was loaded, alongside the new name: no new version.
        var loaded = SpecInput.From(item.Type, item.SpecVersions[0].SpecJson, Today);
        var rename = EditAs(db, item.Id, "NewCo P1 Mk II", "NewCo", loaded);
        (await rename.OnPostAsync()).Should().BeOfType<PageResult>();
        rename.Errors.Should().BeEmpty();
        var renamed = await Items(db).AsNoTracking().SingleAsync(e => e.Id == item.Id);
        renamed.Name.Should().Be("NewCo P1 Mk II");
        renamed.SpecVersions.Should().ContainSingle();
        OemSpecs.Pulsator(renamed, Today).Should().BeEquivalentTo(new PulsatorSpec { RateMin = 58, RateMax = 62 });

        // A changed figure from today is a version of its own; the one from the start stays.
        var edit = EditAs(db, item.Id, "NewCo P1 Mk II", "NewCo", new SpecInput { RateMin = 57, RateMax = 63, EffectiveFrom = Today });
        await edit.OnPostAsync();
        edit.Errors.Should().BeEmpty();
        var versions = await db.EquipmentSpecVersions.AsNoTracking().Where(v => v.EquipmentItemId == item.Id).ToListAsync();
        versions.Select(v => v.EffectiveFrom).Should().BeEquivalentTo(new DateOnly?[] { null, Today });
        OemSpecs.Pulsator(await Items(db).AsNoTracking().SingleAsync(e => e.Id == item.Id), Today)!.RateMin.Should().Be(57);

        (await db.AuditEntries.AnyAsync(a => a.EntityType == nameof(EquipmentItem) && a.EntityKey == item.Id.ToString()
            && a.Operation == "Modified" && a.AfterJson!.Contains("NewCo P1 Mk II"))).Should().BeTrue();
        // The interceptor stores the spec with its quotes escaped inside its own JSON.
        var added = await db.AuditEntries
            .Where(a => a.EntityType == nameof(EquipmentSpecVersion) && a.Operation == "Added")
            .Select(a => a.AfterJson!).ToListAsync();
        added.Should().ContainSingle(j => j.Contains("\\u0022rateMin\\u0022:57"));
    }

    [Fact]
    public async Task A_change_dated_ahead_leaves_todays_spec_until_its_day_and_can_be_replaced_or_removed()
    {
        using var factory = new AuthedWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var item = new EquipmentItem
        {
            Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo",
            SpecVersions = [FromTheStart(new PulsatorSpec { RateMin = 58, RateMax = 62 })],
        };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();
        var ahead = Today.AddDays(30);

        var schedule = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 55, RateMax = 65, EffectiveFrom = ahead });
        await schedule.OnPostAsync();
        schedule.Errors.Should().BeEmpty();
        schedule.Message.Should().Contain(ahead.ToString("d MMM yyyy"));
        schedule.History.Should().HaveCount(2);
        schedule.History[0].InEffect.Should().BeTrue();
        schedule.History[1].Should().Match<EditModel.VersionRow>(v => v.Scheduled && v.EffectiveFrom == ahead);

        var stored = await Items(db).AsNoTracking().SingleAsync(e => e.Id == item.Id);
        OemSpecs.Pulsator(stored, Today)!.RateMin.Should().Be(58);
        OemSpecs.Pulsator(stored, ahead)!.RateMin.Should().Be(55);

        // Saving the same day again replaces that day's version rather than adding another.
        await EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 54, RateMax = 66, EffectiveFrom = ahead }).OnPostAsync();
        var scheduled = await db.EquipmentSpecVersions.AsNoTracking().SingleAsync(v => v.EquipmentItemId == item.Id && v.EffectiveFrom == ahead);
        scheduled.SpecJson.Should().Contain("\"rateMin\":54");

        // The list shows today's spec and when it changes.
        var list = System.Net.WebUtility.HtmlDecode(await factory.CreateClientAs(Roles.SuperAdministrator, "super-oemspecs-2")
            .GetStringAsync("/Admin/Equipment"));
        list.Should().Contain("Rate 58–62 ppm").And.Contain($"Changes {ahead:d MMM yyyy}");

        // A scheduled version can be removed; the one in effect can't.
        var fromTheStart = stored.SpecVersions.Single(v => v.EffectiveFrom is null).Id;
        var refuse = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput());
        (await refuse.OnPostDeleteVersionAsync(fromTheStart)).Should().BeOfType<PageResult>();
        refuse.Errors.Should().ContainSingle().Which.Should().Contain("hasn't come into effect");
        (await EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput()).OnPostDeleteVersionAsync(scheduled.Id))
            .Should().BeOfType<RedirectToPageResult>();
        (await db.EquipmentSpecVersions.CountAsync(v => v.EquipmentItemId == item.Id)).Should().Be(1);
    }

    [Fact]
    public async Task The_list_shows_when_a_seeded_models_spec_changes()
    {
        using var factory = new AuthedWebAppFactory();
        var ahead = Today.AddDays(30);
        using (var scope = factory.Services.CreateScope())
        {
            await Seed.ReferenceDataAsync(scope.ServiceProvider);
            var db = Db(scope);
            var item = await Items(db).SingleAsync(e => e.Name == "DeLaval EP 100/100B 1 cluster EPIC (60/70)");
            var input = SpecInput.From(item.Type, item.SpecVersions.Single().SpecJson, ahead);
            input.RecommendedRate = 60;
            var page = EditAs(db, item.Id, item.Name, item.Brand, input);
            await page.OnPostAsync();
            page.Errors.Should().BeEmpty();
        }

        var list = System.Net.WebUtility.HtmlDecode(await factory.CreateClientAs(Roles.SuperAdministrator, "super-oemspecs-3")
            .GetStringAsync("/Admin/Equipment"));
        var row = list[list.IndexOf("EPIC (60/70)", StringComparison.Ordinal)..];
        row[..row.IndexOf("</tr>", StringComparison.Ordinal)].Should().Contain($"Changes {ahead:d MMM yyyy}");
    }

    [Fact]
    public async Task A_spec_cant_be_dated_before_today_and_an_invalid_spec_saves_nothing()
    {
        using var factory = new AuthedWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var item = new EquipmentItem { Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo" };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();

        var backdated = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 58, RateMax = 62, EffectiveFrom = Today.AddDays(-1) });
        await backdated.OnPostAsync();
        backdated.Errors.Should().ContainSingle().Which.Should().Contain("before today");

        var invalid = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 58, RatioMin = 70, RatioMax = 60, EffectiveFrom = Today });
        (await invalid.OnPostAsync()).Should().BeOfType<PageResult>();
        invalid.Errors.Should().BeEquivalentTo(
            "Rate band: enter both the minimum and the maximum, or neither.",
            "Ratio band: the minimum can't be above the maximum.");

        var undated = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 58, RateMax = 62 });
        await undated.OnPostAsync();
        undated.Errors.Should().ContainSingle().Which.Should().Contain("date the spec applies from");

        (await db.EquipmentSpecVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_admin_pages_say_OEM_Specs_and_show_each_types_spec_fields_and_history()
    {
        using var factory = new AuthedWebAppFactory();
        Guid pulsatorId, shellId;
        using (var scope = factory.Services.CreateScope())
        {
            await Seed.ReferenceDataAsync(scope.ServiceProvider);
            var db = Db(scope);
            pulsatorId = (await db.EquipmentItems.SingleAsync(e => e.Name == Bundled)).Id;
            shellId = (await db.EquipmentItems.FirstAsync(e => e.Type == EquipmentItem.Shell)).Id;
            db.EquipmentItems.Add(new EquipmentItem { Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo" });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientAs(Roles.SuperAdministrator, "super-oemspecs-1");

        var home = await client.GetStringAsync("/Admin");
        home.Should().Contain("OEM Specs").And.NotContain("Equipment catalogs");

        var list = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Equipment"));
        list.Should().Contain("<h1>OEM Specs</h1>");
        list.Should().Contain("Rate 59–61 ppm · Ratio 59–69%");
        list.Should().Contain("1 without a spec");

        var newPage = await client.GetStringAsync("/Admin/Equipment/New");
        newPage.Should().Contain("data-spec-type=\"Pulsator\"").And.Contain("data-spec-type=\"VacuumPump\"");
        newPage.Should().Contain("name=\"Spec.RateMin\"").And.Contain("name=\"Spec.AirFlow\"");
        newPage.Should().MatchRegex($"<input[^>]*value=\"{Today:yyyy-MM-dd}\"[^>]*name=\"Spec\\.EffectiveFrom\"");

        var edit = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Equipment/Edit/{pulsatorId}"));
        edit.Should().Contain("OEM Specs · Pulsator models");
        edit.Should().MatchRegex("name=\"Spec\\.RateMin\"[^>]*value=\"59\"").And.NotContain("Spec.AirFlow");
        edit.Should().Contain("Spec history").And.Contain("The start (legacy AutoRep)").And.Contain("In effect");

        var shell = await client.GetStringAsync($"/Admin/Equipment/Edit/{shellId}");
        shell.Should().NotContain("Spec.").And.NotContain("Spec history");
    }

    [Fact]
    public void Spec_validation_covers_each_types_own_fields()
    {
        var errors = new List<string>();
        new SpecInput { RateMin = 0, RateMax = 60, PhaseB = 120, RecommendedRatio = -1, MaxClustersPerPulsator = new string('x', 51) }
            .Validate(EquipmentItem.Pulsator, errors).Should().BeFalse();
        errors.Should().BeEquivalentTo(
            "Rate band must be above 0 and at most 300 ppm.",
            "Phase b must be between 0 and 100%.",
            "Recommended ratio must be above 0 and at most 100%.",
            "Max clusters per pulsator must be 50 characters or fewer.");

        errors.Clear();
        new SpecInput { MinRpm = 1500, MaxRpm = 1400, AirFlow = 0 }.Validate(EquipmentItem.VacuumPump, errors).Should().BeFalse();
        errors.Should().BeEquivalentTo(
            "Speed range: the minimum rpm can't be above the maximum.",
            "Air flow per rpm must be above 0 and at most 50 L/min.");

        // A pump's fields don't count against a pulsator, and nothing entered is a valid "no spec".
        errors.Clear();
        new SpecInput { AirFlow = -5 }.Validate(EquipmentItem.Pulsator, errors).Should().BeTrue();
        new SpecInput().ToSpecJson(EquipmentItem.Pulsator).Should().BeNull();
        new SpecInput { RateMin = 1, RateMax = 2 }.ToSpecJson(EquipmentItem.Shell).Should().BeNull();
    }

    private static EditModel EditAs(AutorepDbContext db, Guid id, string name, string? brand, SpecInput spec)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "super-1"), new Claim(ClaimTypes.Role, Roles.SuperAdministrator)], "Test"));
        return new EditModel(db)
        {
            Id = id, Name = name, Brand = brand, Spec = spec,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } },
        };
    }
}
