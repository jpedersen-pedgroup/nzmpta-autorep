using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The tester-profile calibration surface: dates live on the Tester (not a farm/test), are
// readable/writable only by the signed-in tester, and round-trip as ISO yyyy-MM-dd.
public class ProfileControllerTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public ProfileControllerTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record CalibrationDto(DateOnly? AirFlowMeters, DateOnly? PulsatorTesters, DateOnly? VacuumGauges);

    private async Task SeedTesterAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        if (db.Users.Any(u => u.Id == id)) return;
        db.Users.Add(new Tester
        {
            Id = id,
            UserName = $"{id}@test.local",
            Email = $"{id}@test.local",
            DisplayName = "Cal Tester",
            CalAirFlowMetersExpiry = new DateOnly(2027, 1, 27),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_returns_own_calibration_dates()
    {
        await SeedTesterAsync("tester-cal-1");
        var client = _factory.CreateClientAs(Roles.Tester, "tester-cal-1");

        var dto = await client.GetFromJsonAsync<CalibrationDto>("/api/profile/calibration");

        dto.Should().NotBeNull();
        dto!.AirFlowMeters.Should().Be(new DateOnly(2027, 1, 27));
        dto.PulsatorTesters.Should().BeNull();
        dto.VacuumGauges.Should().BeNull();
    }

    [Fact]
    public async Task Put_replaces_all_three_dates_and_get_reflects_them()
    {
        await SeedTesterAsync("tester-cal-2");
        var client = _factory.CreateClientAs(Roles.Tester, "tester-cal-2");

        var put = await client.PutAsJsonAsync("/api/profile/calibration",
            new CalibrationDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15), null));
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await client.GetFromJsonAsync<CalibrationDto>("/api/profile/calibration");
        dto!.AirFlowMeters.Should().Be(new DateOnly(2026, 9, 1));
        dto.PulsatorTesters.Should().Be(new DateOnly(2026, 10, 15));
        dto.VacuumGauges.Should().BeNull();
    }

    [Fact]
    public async Task Expired_dates_are_accepted_never_rejected()
    {
        await SeedTesterAsync("tester-cal-3");
        var client = _factory.CreateClientAs(Roles.Tester, "tester-cal-3");

        // Expired equipment must warn, not block — the API cannot refuse a past date.
        var put = await client.PutAsJsonAsync("/api/profile/calibration",
            new CalibrationDto(new DateOnly(2020, 1, 1), null, null));

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await client.GetFromJsonAsync<CalibrationDto>("/api/profile/calibration");
        dto!.AirFlowMeters.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public async Task Anonymous_and_non_tester_roles_are_refused()
    {
        var anon = _factory.CreateClient();
        (await anon.GetAsync("/api/profile/calibration")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, HttpStatusCode.Found);

        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "admin-cal-1");
        (await admin.GetAsync("/api/profile/calibration")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.Found);
    }

    [Fact]
    public async Task Calibration_is_scoped_to_the_caller()
    {
        await SeedTesterAsync("tester-cal-4");
        await SeedTesterAsync("tester-cal-5");
        var t5 = _factory.CreateClientAs(Roles.Tester, "tester-cal-5");

        await t5.PutAsJsonAsync("/api/profile/calibration",
            new CalibrationDto(new DateOnly(2030, 5, 5), new DateOnly(2030, 5, 5), new DateOnly(2030, 5, 5)));

        // tester-cal-4 still sees the seeded value, not tester-cal-5's write.
        var t4 = _factory.CreateClientAs(Roles.Tester, "tester-cal-4");
        var dto = await t4.GetFromJsonAsync<CalibrationDto>("/api/profile/calibration");
        dto!.AirFlowMeters.Should().Be(new DateOnly(2027, 1, 27));
        dto.PulsatorTesters.Should().BeNull();
    }
}
