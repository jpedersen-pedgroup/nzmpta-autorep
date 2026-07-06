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

    [Fact]
    public async Task List_returns_only_farms_in_the_callers_scope()
    {
        Guid mineId, othersId, inactiveId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();

            var mine = new Farm { Name = "List Mine Farm" };
            var others = new Farm { Name = "List Others Farm" };
            var inactive = new Farm { Name = "List Inactive Farm", IsActive = false };
            db.Farms.AddRange(mine, others, inactive);
            db.MachineTests.AddRange(
                new MachineTest { TesterId = "tester-list-1", FarmId = mine.Id, CreatedAt = DateTimeOffset.UtcNow },
                new MachineTest { TesterId = "tester-list-other", FarmId = others.Id, CreatedAt = DateTimeOffset.UtcNow },
                new MachineTest { TesterId = "tester-list-1", FarmId = inactive.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            (mineId, othersId, inactiveId) = (mine.Id, others.Id, inactive.Id);
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-list-1");
        var farms = await client.GetFromJsonAsync<List<FarmResponse>>("/api/farms");

        farms.Should().NotBeNull();
        farms!.Should().Contain(f => f.Id == mineId);
        farms.Should().NotContain(f => f.Id == othersId);   // another tester's farm — not harvestable
        farms.Should().NotContain(f => f.Id == inactiveId); // deactivated farms drop out of the book
    }
}
