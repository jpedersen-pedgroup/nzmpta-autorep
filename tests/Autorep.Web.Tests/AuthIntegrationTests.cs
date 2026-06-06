using System.Net;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

public class AuthIntegrationTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AuthIntegrationTests(AuthedWebAppFactory factory) => _factory = factory;

    // Regression for PR #19: a farm referencing a since-deactivated region / milk company
    // must keep that selection in the edit pickers (otherwise an unrelated save nulls the FK).
    [Fact]
    public async Task Farm_edit_keeps_inactive_region_and_company_in_the_pickers()
    {
        Guid farmId, regionId, companyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var region = new Region { Name = "Decommissioned " + Guid.NewGuid(), Island = "North Island", IsActive = false };
            var company = new MilkSupplyCompany { Name = "Retired Co " + Guid.NewGuid(), IsActive = false };
            db.Regions.Add(region);
            db.MilkSupplyCompanies.Add(company);
            var farm = new Farm { Name = "Farm " + Guid.NewGuid(), RegionId = region.Id, MilkSupplyCompanyId = company.Id };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            farmId = farm.Id; regionId = region.Id; companyId = company.Id;
        }

        var client = _factory.CreateClientAs(Roles.SuperAdministrator);
        var res = await client.GetAsync($"/Admin/Farms/Edit/{farmId}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain(regionId.ToString());   // inactive region still offered (selected)
        html.Should().Contain(companyId.ToString());   // inactive milk company still offered
    }

    // The admin Farm area is for administrators; a Tester must be forbidden.
    [Fact]
    public async Task Admin_farms_is_forbidden_for_a_tester()
    {
        var client = _factory.CreateClientAs(Roles.Tester);
        var res = await client.GetAsync("/Admin/Farms");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // A Company Administrator sees only farms that one of their own company's testers has a
    // completed test against — not other companies' farms.
    [Fact]
    public async Task Farm_index_scopes_a_company_admin_to_their_own_farms()
    {
        string adminId;
        Guid myFarmId, otherFarmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var companyA = new TestingCompany { Name = "Company A " + Guid.NewGuid() };
            var companyB = new TestingCompany { Name = "Company B " + Guid.NewGuid() };
            db.TestingCompanies.AddRange(companyA, companyB);

            var admin = new Tester { Id = Guid.NewGuid().ToString(), UserName = "ca", Email = "ca@x", TestingCompanyId = companyA.Id };
            var testerA = new Tester { Id = Guid.NewGuid().ToString(), UserName = "ta", Email = "ta@x", TestingCompanyId = companyA.Id };
            var testerB = new Tester { Id = Guid.NewGuid().ToString(), UserName = "tb", Email = "tb@x", TestingCompanyId = companyB.Id };
            db.Users.AddRange(admin, testerA, testerB);

            var myFarm = new Farm { Name = "MyFarm " + Guid.NewGuid() };
            var otherFarm = new Farm { Name = "OtherFarm " + Guid.NewGuid() };
            db.Farms.AddRange(myFarm, otherFarm);
            db.MachineTests.Add(new MachineTest { TesterId = testerA.Id, FarmId = myFarm.Id, MarkedCompleteAt = DateTimeOffset.UtcNow });
            db.MachineTests.Add(new MachineTest { TesterId = testerB.Id, FarmId = otherFarm.Id, MarkedCompleteAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            adminId = admin.Id; myFarmId = myFarm.Id; otherFarmId = otherFarm.Id;
        }

        var client = _factory.CreateClientAs(Roles.CompanyAdministrator, adminId);
        var res = await client.GetAsync("/Admin/Farms");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain(myFarmId.ToString());
        html.Should().NotContain(otherFarmId.ToString());
    }
}
