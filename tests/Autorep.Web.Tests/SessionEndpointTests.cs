using System.Net;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// GET /api/session: the identity record the offline shell runs on (Client/db/identity.ts), and the
// authenticated half of the connectivity check. 401-not-302 when signed out is covered with the
// real cookie pipeline in ApiChallengeTests.
public class SessionEndpointTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public SessionEndpointTests(AuthedWebAppFactory factory) => _factory = factory;

    private async Task SeedAsync(string id, string displayName = "Sam Tester", string? certificate = "NZ-1234",
        DateOnly? licence = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        if (db.Users.Any(u => u.Id == id)) return;
        db.Users.Add(new Tester
        {
            Id = id,
            UserName = $"{id}@test.local",
            Email = $"{id}@test.local",
            DisplayName = displayName,
            CertificateNo = certificate,
            LicenceExpiryDate = licence ?? new DateOnly(2027, 3, 1),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Returns_the_signed_in_tester_for_the_device_identity_record()
    {
        await SeedAsync("session-1");
        var client = _factory.CreateClientAs(Roles.Tester, "session-1");

        var res = await client.GetAsync("/api/session");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await ReadJson(res);
        body.GetProperty("testerId").GetString().Should().Be("session-1");
        body.GetProperty("displayName").GetString().Should().Be("Sam Tester");
        body.GetProperty("userName").GetString().Should().Be("session-1");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal(Roles.Tester);
        body.GetProperty("certificateNo").GetString().Should().Be("NZ-1234");
        // DateOnly's JSON shape — the client checks for exactly yyyy-mm-dd.
        body.GetProperty("licenceExpiryDate").GetString().Should().Be("2027-03-01");
        body.GetProperty("syncOnly").GetBoolean().Should().BeFalse();
        body.GetProperty("serverTime").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Is_never_kept_by_a_cache()
    {
        await SeedAsync("session-2");
        var client = _factory.CreateClientAs(Roles.Tester, "session-2");

        var res = await client.GetAsync("/api/session");

        res.Headers.CacheControl!.NoStore.Should().BeTrue("it carries the tester's name and licence");
    }

    [Fact]
    public async Task Reports_a_lapsed_licence_session_as_sync_only()
    {
        await SeedAsync("session-3", licence: new DateOnly(2020, 1, 1));
        var client = _factory.CreateClientAs(Roles.Tester, "session-3",
            claims: $"{LicenceScope.ScopeClaim}={LicenceScope.SyncOnly}");

        var body = await ReadJson(await client.GetAsync("/api/session"));

        body.GetProperty("syncOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Falls_back_to_the_email_when_no_display_name_is_set()
    {
        await SeedAsync("session-4", displayName: "");
        var client = _factory.CreateClientAs(Roles.Tester, "session-4");

        var body = await ReadJson(await client.GetAsync("/api/session"));

        body.GetProperty("displayName").GetString().Should().Be("session-4@test.local");
    }

    [Fact]
    public async Task A_cookie_for_an_account_that_no_longer_exists_is_not_a_session()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "session-deleted");

        var res = await client.GetAsync("/api/session");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Is_for_testers_only()
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator, "session-admin");

        var res = await client.GetAsync("/api/session");

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

// The offline shell can't post the sign-out form (it is a static, cached document with no
// antiforgery token), so its Sign out is a link to this page, which must offer the real form.
public class LogoutPageTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public LogoutPageTests(AuthedWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_offers_a_sign_out_form_instead_of_redirecting()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "logout-1");

        var res = await client.GetAsync("/Account/Logout");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("method=\"post\"");
        html.Should().Contain("__RequestVerificationToken");
    }
}
