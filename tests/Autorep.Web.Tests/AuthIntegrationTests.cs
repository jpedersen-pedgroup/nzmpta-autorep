using System.Net;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Uses the real Identity cookie pipeline (not the TestAuthHandler), because the behaviour under
// test is exactly what the cookie handler does with an unauthenticated request.
public class ApiChallengeTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ApiChallengeTests(WebAppFactory factory) => _factory = factory;

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // An expired cookie used to redirect /api to the login page. `fetch` follows redirects and
    // turns a POST into a GET, so the sync push landed on a 200 HTML login page and read as
    // success — marking the tester's test uploaded when the server never received it.
    [Theory]
    [InlineData("/api/sync/tests")]
    [InlineData("/api/farms")]
    [InlineData("/api/profile/calibration")]
    public async Task Api_answers_401_rather_than_redirecting_to_login(string path)
    {
        var res = await Client().GetAsync(path);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.Location.Should().BeNull("an API caller must never be sent to a login page");
    }

    [Fact]
    public async Task Api_post_answers_401_rather_than_redirecting()
    {
        var res = await Client().PostAsync("/api/sync/tests",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The page flow must keep redirecting — only /api changed.
    [Fact]
    public async Task Pages_still_redirect_to_login()
    {
        var res = await Client().GetAsync("/App/Tests");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }
}

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

    // The Farms Edit guard must hold on both directions of the shared FarmScope predicate:
    // Forbid for an out-of-scope farm, OK for a farm the company set up but hasn't tested yet.
    [Fact]
    public async Task Farm_edit_forbids_a_company_admin_for_an_out_of_scope_farm_and_allows_their_own()
    {
        string adminId;
        Guid foreignFarmId, ownUntestedFarmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var companyA = new TestingCompany { Name = "Edit-Guard Co A " + Guid.NewGuid() };
            var companyB = new TestingCompany { Name = "Edit-Guard Co B " + Guid.NewGuid() };
            db.TestingCompanies.AddRange(companyA, companyB);

            var admin = new Tester { Id = Guid.NewGuid().ToString(), UserName = "eg-ca", Email = "eg-ca@x", TestingCompanyId = companyA.Id };
            var testerB = new Tester { Id = Guid.NewGuid().ToString(), UserName = "eg-tb", Email = "eg-tb@x", TestingCompanyId = companyB.Id };
            db.Users.AddRange(admin, testerB);

            var foreignFarm = new Farm { Name = "ForeignFarm " + Guid.NewGuid() };
            var ownUntestedFarm = new Farm { Name = "OwnUntestedFarm " + Guid.NewGuid(), CreatedByTestingCompanyId = companyA.Id };
            db.Farms.AddRange(foreignFarm, ownUntestedFarm);
            db.MachineTests.Add(new MachineTest { TesterId = testerB.Id, FarmId = foreignFarm.Id, MarkedCompleteAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            adminId = admin.Id; foreignFarmId = foreignFarm.Id; ownUntestedFarmId = ownUntestedFarm.Id;
        }

        var client = _factory.CreateClientAs(Roles.CompanyAdministrator, adminId);

        (await client.GetAsync($"/Admin/Farms/Edit/{foreignFarmId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "another company's farm must not be editable");
        (await client.GetAsync($"/Admin/Farms/Edit/{ownUntestedFarmId}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "a farm the company set up (even untested) is in its scope");
    }

    // The All-tests farm deep-link heading must not disclose another company's farm name for a
    // guessed farm id; a Super-Administrator still resolves any farm's name.
    [Fact]
    public async Task Admin_tests_farm_deep_link_hides_a_foreign_farms_name_from_a_company_admin()
    {
        string adminId;
        Guid foreignFarmId;
        string foreignFarmName = "SecretFarm " + Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var companyA = new TestingCompany { Name = "DeepLink Co A " + Guid.NewGuid() };
            var companyB = new TestingCompany { Name = "DeepLink Co B " + Guid.NewGuid() };
            db.TestingCompanies.AddRange(companyA, companyB);

            var admin = new Tester { Id = Guid.NewGuid().ToString(), UserName = "dl-ca", Email = "dl-ca@x", TestingCompanyId = companyA.Id };
            var testerB = new Tester { Id = Guid.NewGuid().ToString(), UserName = "dl-tb", Email = "dl-tb@x", TestingCompanyId = companyB.Id };
            db.Users.AddRange(admin, testerB);

            var foreignFarm = new Farm { Name = foreignFarmName };
            db.Farms.Add(foreignFarm);
            db.MachineTests.Add(new MachineTest { TesterId = testerB.Id, FarmId = foreignFarm.Id, MarkedCompleteAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            adminId = admin.Id; foreignFarmId = foreignFarm.Id;
        }

        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, adminId);
        var res = await companyAdmin.GetAsync($"/Admin/Tests?farmId={foreignFarmId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().NotContain(foreignFarmName);

        var superAdmin = _factory.CreateClientAs(Roles.SuperAdministrator);
        var saRes = await superAdmin.GetAsync($"/Admin/Tests?farmId={foreignFarmId}");
        (await saRes.Content.ReadAsStringAsync()).Should().Contain(foreignFarmName);
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
