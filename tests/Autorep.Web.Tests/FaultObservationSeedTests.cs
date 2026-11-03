using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The standard fault list: seeded into an empty database, then the SuperAdmin's. The vac pump
// running check's "Water Flow Rate Poor" became "Water/Oil Flow Rate Poor" (Jono, 2 Nov 2026), and a
// one-off rename carries that to a database seeded before then.
public class FaultObservationSeedTests
{
    private const string Category = "VPOWWaterFlowRate";
    private const string Old = "Water Flow Rate Poor";
    private const string New = "Water/Oil Flow Rate Poor";

    // A database of its own per test, with the services the reference seed needs.
    private static ServiceProvider NewServices()
    {
        var name = "faultseed-" + Guid.NewGuid();
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

    private static async Task<List<FaultObservation>> RowsAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AutorepDbContext>()
            .FaultObservations.Where(f => f.Category == Category).ToListAsync();
    }

    private static async Task AddAsync(ServiceProvider sp, params FaultObservation[] rows)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        db.FaultObservations.AddRange(rows);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_fresh_database_gets_the_water_oil_wording()
    {
        using var sp = NewServices();
        await SeedAsync(sp);

        var rows = await RowsAsync(sp);
        rows.Select(r => r.Name).Should().BeEquivalentTo(New, "Restriction Inside Vacuum Pump", "Restriction Inside Exhaust");
        rows.Single(r => r.Name == New).Severity.Should().Be("Minor");
    }

    [Fact]
    public async Task A_database_seeded_before_the_change_has_the_row_renamed_once()
    {
        // Staging: the list was seeded with the old wording.
        using var sp = NewServices();
        var seededAt = DateTimeOffset.UtcNow.AddDays(-30);
        await AddAsync(sp,
            new FaultObservation { Category = Category, Name = Old, Severity = "Minor", UpdatedAt = seededAt },
            new FaultObservation { Category = Category, Name = "Restriction Inside Vacuum Pump", Severity = "Minor", UpdatedAt = seededAt });

        await SeedAsync(sp);

        var rows = await RowsAsync(sp);
        rows.Should().HaveCount(2);
        var renamed = rows.Single(r => r.Name == New);
        renamed.Severity.Should().Be("Minor");
        renamed.UpdatedAt.Should().BeAfter(seededAt); // so devices fetch the new list
        rows.Single(r => r.Name != New).UpdatedAt.Should().Be(seededAt);

        // It runs once: if the SuperAdmin brings the old wording back, a restart leaves it alone.
        await AddAsync(sp, new FaultObservation { Category = Category, Name = Old, Severity = "Major" });
        await SeedAsync(sp);
        (await RowsAsync(sp)).Select(r => r.Name).Should().BeEquivalentTo(New, Old, "Restriction Inside Vacuum Pump");
    }

    [Fact]
    public async Task A_row_the_SuperAdmin_has_reworded_is_left_alone()
    {
        using var sp = NewServices();
        await AddAsync(sp, new FaultObservation { Category = Category, Name = "Oil/Water Flow Too Low", Severity = "Major" });

        await SeedAsync(sp);

        (await RowsAsync(sp)).Should().ContainSingle().Which.Name.Should().Be("Oil/Water Flow Too Low");
    }

    [Fact]
    public async Task The_old_wording_stays_if_the_new_one_is_already_on_the_list()
    {
        // Both on the list (the SuperAdmin added the new wording by hand): renaming would clash.
        using var sp = NewServices();
        await AddAsync(sp,
            new FaultObservation { Category = Category, Name = Old, Severity = "Minor" },
            new FaultObservation { Category = Category, Name = New, Severity = "Minor" });

        await SeedAsync(sp);

        (await RowsAsync(sp)).Select(r => r.Name).Should().BeEquivalentTo(Old, New);
    }
}
