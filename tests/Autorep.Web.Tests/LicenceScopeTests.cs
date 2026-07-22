using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Domain;
using FluentAssertions;

namespace Autorep.Web.Tests;

// A lapsed tester licence must not strand work: the session survives, restricted to flushing
// Machine Tests already captured on the device (only their owner can ever push them).
public class LicenceScopeTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public LicenceScopeTests(AuthedWebAppFactory factory) => _factory = factory;

    private const string SyncOnlyClaim = $"{LicenceScope.ScopeClaim}={LicenceScope.SyncOnly}";

    private static readonly DateOnly Today = new(2026, 7, 22);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    [Fact]
    public void An_expired_pure_tester_is_sync_only()
    {
        LicenceScope.IsSyncOnly(Yesterday, new[] { Roles.Tester }, Today).Should().BeTrue();
    }

    [Fact]
    public void A_current_or_unset_licence_is_never_sync_only()
    {
        LicenceScope.IsSyncOnly(Tomorrow, new[] { Roles.Tester }, Today).Should().BeFalse();
        LicenceScope.IsSyncOnly(Today, new[] { Roles.Tester }, Today).Should().BeFalse("expiry day is still valid");
        LicenceScope.IsSyncOnly(null, new[] { Roles.Tester }, Today).Should().BeFalse();
    }

    // An administrator's access doesn't hang off a testing licence.
    [Theory]
    [InlineData(Roles.CompanyAdministrator)]
    [InlineData(Roles.SuperAdministrator)]
    public void An_expired_licence_does_not_restrict_a_multi_role_user(string adminRole)
    {
        LicenceScope.IsSyncOnly(Yesterday, new[] { Roles.Tester, adminRole }, Today).Should().BeFalse();
    }

    [Fact]
    public async Task A_sync_only_session_cannot_reach_the_tester_app()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "lapsed-1", SyncOnlyClaim);

        foreach (var path in new[] { "/App", "/App/Tests", "/App/Tests/New", "/App/Tests/Wizard" })
        {
            (await client.GetAsync(path)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{path} must be closed to a lapsed licence");
        }
    }

    [Fact]
    public async Task A_licensed_tester_still_reaches_the_tester_app()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "current-1");
        (await client.GetAsync("/App/Tests")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_flush_page_is_open_to_a_sync_only_session_and_redirects_everyone_else()
    {
        var lapsed = _factory.CreateClientAs(Roles.Tester, "lapsed-2", SyncOnlyClaim);
        (await lapsed.GetAsync("/Account/FinishSync")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A licensed tester has the whole app — don't strand them on the cut-down page.
        var current = _factory.CreateClientAs(Roles.Tester, "current-2");
        var res = await current.GetAsync("/Account/FinishSync");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.OriginalString.Should().Be("/App");
    }

    // The whole point: the session exists so queued work can still be surrendered, and the sync
    // surface has no licence check of its own.
    [Fact]
    public async Task A_sync_only_session_can_still_push_and_pull_tests()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "lapsed-3", SyncOnlyClaim);

        var push = await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = Guid.NewGuid(),
            farmName = "Stranded Farm " + Guid.NewGuid(),
            notes = (string?)null,
            markedCompleteAt = (DateTimeOffset?)null,
            createdAt = DateTimeOffset.UtcNow,
            config = (object?)null,
        });
        push.StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.GetAsync("/api/sync/tests")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // The landing page is the PWA's start_url, so it must not dump a lapsed tester on a 403.
    [Fact]
    public async Task The_landing_page_sends_a_sync_only_session_to_the_flush_page()
    {
        var lapsed = _factory.CreateClientAs(Roles.Tester, "lapsed-4", SyncOnlyClaim);
        var res = await lapsed.GetAsync("/");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.OriginalString.Should().Be("/Account/FinishSync");

        var current = _factory.CreateClientAs(Roles.Tester, "current-3");
        var ok = await current.GetAsync("/");
        ok.Headers.Location!.OriginalString.Should().Be("/App");
    }
}
