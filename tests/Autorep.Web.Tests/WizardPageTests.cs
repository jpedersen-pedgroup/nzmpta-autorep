using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Domain.Wizard;
using Autorep.Web.Pages.App.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Tests;

// Page-model tests for the server-rendered wizard shell (resolver-driven rail + config step).
public class WizardPageTests
{
    private const string TesterId = "tester-1";

    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("wizard-" + Guid.NewGuid()).Options);

    private static WizardModel ModelFor(AutorepDbContext db, string testerId = TesterId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, testerId) }, "Test"));
        return new WizardModel(db)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static async Task<MachineTest> SeedTestAsync(AutorepDbContext db, string testerId = TesterId)
    {
        var farm = new Farm { Name = "Sunny Acres", IsActive = true };
        db.Farms.Add(farm);
        var test = new MachineTest { TesterId = testerId, FarmId = farm.Id };
        db.MachineTests.Add(test);
        await db.SaveChangesAsync();
        return test;
    }

    [Fact]
    public async Task OnGet_loads_the_test_and_resolves_the_plan()
    {
        using var db = NewDb();
        var test = await SeedTestAsync(db);
        var model = ModelFor(db);
        model.Id = test.Id;

        var result = await model.OnGetAsync();

        result.Should().BeOfType<PageResult>();
        model.Plan.Steps.Should().HaveCount(10);
        model.Current.Should().Be(WizardStep.MachineConfiguration);
    }

    [Fact]
    public async Task OnGet_redirects_when_test_not_owned_by_current_tester()
    {
        using var db = NewDb();
        var test = await SeedTestAsync(db, "someone-else");
        var model = ModelFor(db, TesterId);
        model.Id = test.Id;

        var result = await model.OnGetAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/App/Tests/Index");
    }

    [Fact]
    public async Task OnPostConfig_persists_configuration_and_advances_to_next_step()
    {
        using var db = NewDb();
        var test = await SeedTestAsync(db);
        var model = ModelFor(db);
        model.Id = test.Id;
        model.Config = new WizardModel.ConfigInput
        {
            PlantType = PlantType.Rotary,
            ClusterCount = 54,
            HasAcr = true,
        };

        var result = await model.OnPostConfigAsync();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.RouteValues!["step"].Should().Be(nameof(WizardStep.VisualFaultsPreStart));

        var cfg = await db.MachineConfigurations.SingleAsync(c => c.MachineTestId == test.Id);
        cfg.PlantType.Should().Be(PlantType.Rotary);
        cfg.ClusterCount.Should().Be(54);
        cfg.HasAcr.Should().BeTrue();
    }

    [Fact]
    public async Task Completed_tracks_setup_then_configuration_after_save()
    {
        using var db = NewDb();
        var test = await SeedTestAsync(db);

        var before = ModelFor(db);
        before.Id = test.Id;
        await before.OnGetAsync();
        before.Completed.Should().Contain(WizardStep.Setup);
        before.Completed.Should().NotContain(WizardStep.MachineConfiguration);

        var save = ModelFor(db);
        save.Id = test.Id;
        save.Config = new WizardModel.ConfigInput { ClusterCount = 20 };
        await save.OnPostConfigAsync();

        var after = ModelFor(db);
        after.Id = test.Id;
        await after.OnGetAsync();
        after.Completed.Should().Contain(WizardStep.MachineConfiguration);
    }
}
