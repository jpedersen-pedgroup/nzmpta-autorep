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

    // ---- Tester details ----------------------------------------------------------------------

    private sealed record TesterDetailsDto(string Name, string? Phone, string? RegistrationNumber, DateOnly? RegistrationExpiry);

    [Fact]
    public async Task Tester_returns_the_details_the_report_names()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.Users.Add(new Tester
            {
                Id = "tester-details-1", UserName = "td1@test.local", Email = "td1@test.local",
                DisplayName = "Alan Tester", PhoneNumber = "021 752 097", CertificateNo = "594",
                LicenceExpiryDate = new DateOnly(2026, 10, 31),
            });
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClientAs(Roles.Tester, "tester-details-1");

        var dto = await client.GetFromJsonAsync<TesterDetailsDto>("/api/profile/tester");

        dto.Should().Be(new TesterDetailsDto("Alan Tester", "021 752 097", "594", new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public async Task Tester_without_a_display_name_is_named_by_email()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.Users.Add(new Tester { Id = "tester-details-2", UserName = "td2@test.local", Email = "td2@test.local" });
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClientAs(Roles.Tester, "tester-details-2");

        (await client.GetFromJsonAsync<TesterDetailsDto>("/api/profile/tester"))!.Name.Should().Be("td2@test.local");
    }

    // ---- Company branding ------------------------------------------------------------------

    private sealed record CompanyBrandingDto(Guid Id, string Name, string? Logo);

    private async Task<Guid> SeedCompanyTesterAsync(string testerId, byte[]? logo, string name = "Brand Co")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = name, LogoData = logo, LogoContentType = logo is null ? null : "image/png" };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = testerId, UserName = $"{testerId}@test.local", DisplayName = "Brand Tester", TestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task SetLogoAsync(Guid companyId, byte[] logo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        (await db.TestingCompanies.FindAsync(companyId))!.LogoData = logo;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Company_returns_the_testers_company_with_its_logo_as_a_data_url()
    {
        var companyId = await SeedCompanyTesterAsync("brand-1", [1, 2, 3], "Sample Testing Co");
        var client = _factory.CreateClientAs(Roles.Tester, "brand-1");

        var dto = await client.GetFromJsonAsync<CompanyBrandingDto>("/api/profile/company");

        dto!.Id.Should().Be(companyId);
        dto.Name.Should().Be("Sample Testing Co");
        dto.Logo.Should().Be("data:image/png;base64,AQID");
    }

    [Fact]
    public async Task Company_without_a_logo_returns_a_null_logo()
    {
        await SeedCompanyTesterAsync("brand-2", null);
        var client = _factory.CreateClientAs(Roles.Tester, "brand-2");

        var dto = await client.GetFromJsonAsync<CompanyBrandingDto>("/api/profile/company");

        dto!.Logo.Should().BeNull();
    }

    [Fact]
    public async Task Company_is_no_content_for_a_tester_without_a_company()
    {
        await SeedTesterAsync("brand-3");
        var client = _factory.CreateClientAs(Roles.Tester, "brand-3");

        (await client.GetAsync("/api/profile/company")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Company_answers_304_until_the_logo_changes()
    {
        var companyId = await SeedCompanyTesterAsync("brand-4", [1, 2, 3]);
        var client = _factory.CreateClientAs(Roles.Tester, "brand-4");

        var first = await client.GetAsync("/api/profile/company");
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/profile/company");
        again.Headers.IfNoneMatch.Add(etag!);
        var unchanged = await client.SendAsync(again);
        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified, "the device already holds this logo");

        await SetLogoAsync(companyId, [9, 9, 9]);
        var afterChange = new HttpRequestMessage(HttpMethod.Get, "/api/profile/company");
        afterChange.Headers.IfNoneMatch.Add(etag!);
        var changed = await client.SendAsync(afterChange);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await changed.Content.ReadFromJsonAsync<CompanyBrandingDto>())!.Logo.Should().Be("data:image/png;base64,CQkJ");
    }
}
