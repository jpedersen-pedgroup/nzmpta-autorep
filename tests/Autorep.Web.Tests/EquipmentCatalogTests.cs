using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The equipment catalogs a Device syncs for its Machine Configuration dropdowns. The pump catalogs
// are seeded from the legacy OEM tables (VPModel / MilkPumps) with the make in Brand, which is what
// drives the make -> model dropdowns on the device.
public class EquipmentCatalogTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public EquipmentCatalogTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record ItemDto(string Type, string Name, string? Brand);
    private sealed record EquipmentResponse(DateTimeOffset? Version, List<ItemDto> Items);

    [Fact]
    public async Task Pump_catalogs_are_seeded_and_served_with_their_make()
    {
        // This factory seeds nothing on startup, so run the real reference seed first.
        using (var scope = _factory.Services.CreateScope())
        {
            await Seed.ReferenceDataAsync(scope.ServiceProvider);
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-equipment-1");
        var res = await client.GetFromJsonAsync<EquipmentResponse>("/api/equipment");
        res.Should().NotBeNull();

        var vacuum = res!.Items.Where(i => i.Type == "VacuumPump").ToList();
        vacuum.Should().HaveCount(140);
        vacuum.Should().OnlyContain(i => i.Brand != null); // without a make the model dropdown can't filter
        vacuum.Should().Contain(i => i.Brand == "De Laval" && i.Name == "DVP1600");

        res.Items.Count(i => i.Type == "ReleaserPump").Should().Be(45);
        res.Items.Should().Contain(i => i.Type == "ReleaserPump" && i.Brand == "READ");

        // Regulator types have no legacy list; the catalog starts empty and the SuperAdmin fills it.
        res.Items.Should().NotContain(i => i.Type == "Regulator");
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_a_catalog()
    {
        // Seed-if-type-empty: the pump catalogs land in existing databases on the next start, but a
        // restart after that must not stack a second copy on top.
        using var scope = _factory.Services.CreateScope();
        await Seed.ReferenceDataAsync(scope.ServiceProvider);
        await Seed.ReferenceDataAsync(scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        db.EquipmentItems.Count(e => e.Type == "VacuumPump").Should().Be(140);
        db.EquipmentItems.Count(e => e.Type == "ReleaserPump").Should().Be(45);
    }
}
