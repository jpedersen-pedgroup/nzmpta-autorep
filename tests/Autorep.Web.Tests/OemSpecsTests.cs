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

// The OEM Specs: the pulsator and vacuum pump specs the SuperAdmin maintains, seeded once from the
// specs the device bundled, served to devices with each item's id, and edited on the admin pages.
public class OemSpecsTests
{
    private const string Bundled = "Electronic (Simultaneous)"; // Dairymaster: rate 59–61, ratio 59–69

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

    [Fact]
    public async Task Seed_gives_every_pulsator_model_and_vacuum_pump_its_bundled_spec()
    {
        using var sp = NewServices();
        await SeedAsync(sp);

        using var scope = sp.CreateScope();
        var db = Db(scope);
        var pulsators = await db.EquipmentItems.Where(e => e.Type == EquipmentItem.Pulsator).ToListAsync();
        var pumps = await db.EquipmentItems.Where(e => e.Type == EquipmentItem.VacuumPump).ToListAsync();
        pulsators.Should().HaveCount(127).And.OnlyContain(e => e.SpecJson != null && e.IsActive);
        pumps.Should().HaveCount(140).And.OnlyContain(e => e.SpecJson != null && e.IsActive);

        var band = OemSpecs.Pulsator(pulsators.Single(e => e.Name == Bundled))!;
        band.Should().BeEquivalentTo(new PulsatorSpec
        {
            RateMin = 59, RateMax = 61, RatioMin = 59, RatioMax = 69, PhaseB = 40, PhaseD = 20,
            MaxChamberVacuum = 10, RecommendedRate = 60, RecommendedRatio = 60, MaxClustersPerPulsator = "2",
        });
        OemSpecs.VacuumPump(pumps.Single(e => e.Brand == "De Laval" && e.Name == "DVP1600"))!
            .Should().BeEquivalentTo(new VacuumPumpSpec { MinRpm = 275, MaxRpm = 1400, AirFlow = 1.15, MotorSizeFactor = 35 });

        // Only pulsators and vacuum pumps carry specs.
        (await db.EquipmentItems.AnyAsync(e => !OemSpecs.SpecTypes.Contains(e.Type) && e.SpecJson != null)).Should().BeFalse();
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
                    SpecJson = OemSpecs.Serialize(new VacuumPumpSpec { AirFlow = 9 }),
                });
            await db.SaveChangesAsync();
        }

        await SeedAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            var items = await db.EquipmentItems.Where(e => OemSpecs.SpecTypes.Contains(e.Type)).ToListAsync();
            OemSpecs.Pulsator(items.Single(e => e.Name == "electronic  (simultaneous)"))!.RateMax.Should().Be(61);
            items.Single(e => e.Name == "EPIC 60/65 (renamed)").SpecJson.Should().BeNull();
            OemSpecs.VacuumPump(items.Single(e => e.Brand == "DE LAVAL"))!.AirFlow.Should().Be(9);

            // The bundled rows that matched nothing are kept, with their specs, as deactivated items:
            // in the admin's view, out of testers' dropdowns.
            var added = items.Where(e => !e.IsActive).ToList();
            added.Should().HaveCount(126 + 139).And.OnlyContain(e => e.SpecJson != null);
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
            var pump = await db.EquipmentItems.SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP1600");
            pump.SpecJson = OemSpecs.Serialize(new VacuumPumpSpec { AirFlow = 2 });
            (await db.EquipmentItems.SingleAsync(e => e.Name == Bundled)).SpecJson = null;
            db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP2300"));
            await db.SaveChangesAsync();
        }

        await SeedAsync(sp);
        await SeedAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = Db(scope);
            var pump = await db.EquipmentItems.SingleAsync(e => e.Type == EquipmentItem.VacuumPump && e.Name == "DVP1600");
            OemSpecs.VacuumPump(pump)!.AirFlow.Should().Be(2);
            (await db.EquipmentItems.SingleAsync(e => e.Name == Bundled)).SpecJson.Should().BeNull();
            (await db.EquipmentItems.AnyAsync(e => e.Name == "DVP2300")).Should().BeFalse();
            (await db.EquipmentItems.CountAsync(e => e.Type == EquipmentItem.Pulsator)).Should().Be(127);
        }
    }

    [Fact]
    public async Task Devices_get_each_item_with_its_id_and_spec_and_deactivated_models_for_their_specs()
    {
        using var factory = new AuthedWebAppFactory();
        Guid deactivated;
        using (var scope = factory.Services.CreateScope())
        {
            await Seed.ReferenceDataAsync(scope.ServiceProvider);
            var db = Db(scope);
            var item = await db.EquipmentItems.SingleAsync(e => e.Name == Bundled);
            item.IsActive = false;
            await db.SaveChangesAsync();
            deactivated = item.Id;
        }

        var client = factory.CreateClientAs(Roles.Tester, "tester-oemspecs-1");
        var res = await client.GetFromJsonAsync<JsonElement>("/api/equipment");
        var items = res.GetProperty("items").EnumerateArray().ToList();
        var inactive = res.GetProperty("inactive").EnumerateArray().ToList();

        var pump = items.Single(i => i.GetProperty("brand").GetString() == "De Laval" && i.GetProperty("name").GetString() == "DVP1600");
        pump.GetProperty("id").GetGuid().Should().NotBeEmpty();
        pump.GetProperty("spec").GetProperty("airFlow").GetDouble().Should().Be(1.15);
        // A type without specs still says so, so a device can tell "no spec" from an older server.
        items.First(i => i.GetProperty("type").GetString() == "Shell").GetProperty("spec").ValueKind.Should().Be(JsonValueKind.Null);

        items.Should().NotContain(i => i.GetProperty("id").GetGuid() == deactivated);
        var kept = inactive.Single(i => i.GetProperty("id").GetGuid() == deactivated);
        kept.GetProperty("spec").GetProperty("rateMin").GetDouble().Should().Be(59);
    }

    [Fact]
    public async Task A_model_added_with_its_spec_is_served_to_devices()
    {
        using var factory = new AuthedWebAppFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var page = new NewModel(Db(scope))
            {
                Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo",
                Spec = new SpecInput { RateMin = 58, RateMax = 62, RatioMin = 60, RatioMax = 70, MaxClustersPerPulsator = " 2 " },
                PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            };
            (await page.OnPostAsync()).Should().BeOfType<RedirectToPageResult>();
            page.Errors.Should().BeEmpty();
        }

        var client = factory.CreateClientAs(Roles.Tester, "tester-oemspecs-2");
        var res = await client.GetFromJsonAsync<JsonElement>("/api/equipment");
        var spec = res.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "NewCo P1").GetProperty("spec");
        spec.GetProperty("rateMin").GetDouble().Should().Be(58);
        spec.GetProperty("ratioMax").GetDouble().Should().Be(70);
        spec.GetProperty("maxClustersPerPulsator").GetString().Should().Be("2");
        spec.GetProperty("phaseB").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Renaming_an_item_keeps_its_spec_and_a_spec_edit_is_audited()
    {
        using var factory = new AuthedWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var item = new EquipmentItem
        {
            Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo",
            SpecJson = OemSpecs.Serialize(new PulsatorSpec { RateMin = 58, RateMax = 62 }),
        };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();

        // The form posts the spec back as it was loaded, alongside the new name.
        var rename = EditAs(db, item.Id, "NewCo P1 Mk II", "NewCo", SpecInput.From(item));
        (await rename.OnPostAsync()).Should().BeOfType<PageResult>();
        rename.Errors.Should().BeEmpty();
        var renamed = await db.EquipmentItems.AsNoTracking().SingleAsync(e => e.Id == item.Id);
        renamed.Name.Should().Be("NewCo P1 Mk II");
        OemSpecs.Pulsator(renamed).Should().BeEquivalentTo(new PulsatorSpec { RateMin = 58, RateMax = 62 });

        var edit = EditAs(db, item.Id, "NewCo P1 Mk II", "NewCo", new SpecInput { RateMin = 57, RateMax = 63 });
        await edit.OnPostAsync();
        var audit = await db.AuditEntries
            .Where(a => a.EntityType == nameof(EquipmentItem) && a.EntityKey == item.Id.ToString() && a.Operation == "Modified")
            .OrderBy(a => a.Id)
            .ToListAsync();
        audit.Should().HaveCount(2);
        audit[1].BeforeJson.Should().Contain("\\u0022rateMin\\u0022:58");
        audit[1].AfterJson.Should().Contain("\\u0022rateMin\\u0022:57");
    }

    [Fact]
    public async Task An_invalid_spec_is_refused_and_nothing_is_saved()
    {
        using var factory = new AuthedWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var item = new EquipmentItem { Type = EquipmentItem.Pulsator, Name = "NewCo P1", Brand = "NewCo" };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();

        var page = EditAs(db, item.Id, "NewCo P1", "NewCo", new SpecInput { RateMin = 58, RatioMin = 70, RatioMax = 60 });
        (await page.OnPostAsync()).Should().BeOfType<PageResult>();
        page.Errors.Should().BeEquivalentTo(
            "Rate band: enter both the minimum and the maximum, or neither.",
            "Ratio band: the minimum can't be above the maximum.");
        (await db.EquipmentItems.AsNoTracking().SingleAsync(e => e.Id == item.Id)).SpecJson.Should().BeNull();
    }

    [Fact]
    public async Task The_admin_pages_say_OEM_Specs_and_show_each_types_spec_fields()
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

        var edit = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Equipment/Edit/{pulsatorId}"));
        edit.Should().Contain("OEM Specs · Pulsator models");
        edit.Should().MatchRegex("name=\"Spec\\.RateMin\"[^>]*value=\"59\"").And.NotContain("Spec.AirFlow");

        var shell = await client.GetStringAsync($"/Admin/Equipment/Edit/{shellId}");
        shell.Should().NotContain("Spec.");
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
