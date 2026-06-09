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
}
