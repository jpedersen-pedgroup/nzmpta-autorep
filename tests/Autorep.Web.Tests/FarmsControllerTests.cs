using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

public class FarmsControllerTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public FarmsControllerTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record FarmResponse(
        Guid Id, string Name, string? SupplyNumber, string? Town,
        string? RegionName, string? MilkCompanyName, string? FarmerName);

    [Fact]
    public async Task Get_returns_farm_details_with_region_and_company_names()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var region = new Region { Name = "Waikato " + Guid.NewGuid(), Island = "North Island" };
            var company = new MilkSupplyCompany { Name = "Fonterra " + Guid.NewGuid() };
            db.Regions.Add(region);
            db.MilkSupplyCompanies.Add(company);
            var farm = new Farm
            {
                Name = "Detail Farm",
                SupplyNumber = "12345",
                Town = "Hamilton",
                RegionId = region.Id,
                MilkSupplyCompanyId = company.Id,
                FarmerName = "Jo Farmer",
            };
            db.Farms.Add(farm);
            // A tester may only fetch a farm they (or their company) have tested.
            db.MachineTests.Add(new MachineTest
            {
                TesterId = "tester-farm-1",
                FarmId = farm.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-farm-1");
        var dto = await client.GetFromJsonAsync<FarmResponse>($"/api/farms/{farmId}");

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Detail Farm");
        dto.SupplyNumber.Should().Be("12345");
        dto.Town.Should().Be("Hamilton");
        dto.RegionName.Should().StartWith("Waikato");
        dto.MilkCompanyName.Should().StartWith("Fonterra");
        dto.FarmerName.Should().Be("Jo Farmer");
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_farm()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-farm-2");
        var res = await client.GetAsync($"/api/farms/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The shared FarmScope predicate includes farms the company set up but hasn't tested yet,
    // so the wizard can snapshot a freshly added farm before its first test is synced.
    [Fact]
    public async Task Get_returns_a_farm_created_by_the_testers_company_even_before_any_test()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var company = new TestingCompany { Name = "Created-By Co " + Guid.NewGuid() };
            db.TestingCompanies.Add(company);
            db.Users.Add(new Tester { Id = "tester-createdby-1", UserName = "tester-createdby-1", TestingCompanyId = company.Id });
            var farm = new Farm { Name = "Freshly Added Farm", CreatedByTestingCompanyId = company.Id };
            db.Farms.Add(farm); // deliberately no MachineTest yet
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-createdby-1");
        var dto = await client.GetFromJsonAsync<FarmResponse>($"/api/farms/{farmId}");

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Freshly Added Farm");
    }

    [Fact]
    public async Task Get_returns_404_for_a_farm_the_tester_has_no_relationship_with()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var farm = new Farm { Name = "Other-Co Farm", FarmerName = "Private Contact" };
            db.Farms.Add(farm);
            // Linked only to a different tester — must not be harvestable by an unrelated tester.
            db.MachineTests.Add(new MachineTest
            {
                TesterId = "some-other-tester",
                FarmId = farm.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-no-access");
        var res = await client.GetAsync($"/api/farms/{farmId}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
