using System.Net;
using Autorep.Web.Domain;
using FluentAssertions;

namespace Autorep.Web.Tests;

/// <summary>The middleware on its own, behind the header scheme: it acts on the claim and
/// nothing else, so a principal without it (any other sign-in path) is untouched.</summary>
public class MfaEnrolmentMiddlewareTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public MfaEnrolmentMiddlewareTests(AuthedWebAppFactory factory) => _factory = factory;

    private const string MustEnrol = MfaPolicy.ClaimType + "=" + MfaPolicy.EnrolmentRequired;

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Testers")]
    [InlineData("/Help")]
    public async Task Pages_redirect_an_unenrolled_required_account_to_setup(string path)
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator, claims: MustEnrol);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Account/SetupAuthenticator");
    }

    [Fact]
    public async Task Api_answers_403_with_a_reason_rather_than_redirecting()
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator, claims: MustEnrol);
        var response = await client.GetAsync("/api/tests");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("two-factor-enrolment-required");
    }

    [Theory]
    [InlineData("/Account/Manage")]
    [InlineData("/Account/SetupAuthenticator")]
    [InlineData("/health")]
    public async Task Account_pages_and_health_stay_reachable(string path)
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator, claims: MustEnrol);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Without_the_claim_nothing_changes()
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator);
        (await client.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_required_role_session_past_30_days_since_its_code_is_signed_out()
    {
        var stale = _factory.CreateClientAs(Roles.SuperAdministrator, mfaAt: DateTimeOffset.UtcNow.AddDays(-31));

        var page = await stale.GetAsync("/Admin/Testers");
        page.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Headers.Location!.ToString().Should().StartWith("/Account/Login?returnUrl=%2FAdmin%2FTesters&reason=mfa-expired");
        // The application cookie is cleared, not merely bypassed.
        page.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal) && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        var api = await stale.GetAsync("/api/tests");
        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.Content.ReadAsStringAsync()).Should().Contain("two-factor-expired");
    }

    [Fact]
    public async Task A_required_role_ticket_with_no_stamp_at_all_is_signed_out_too()
    {
        // Every cookie issued before this feature shipped looks like this; with the application
        // cookie sliding, "no stamp means fresh" would have let it live forever.
        var legacy = _factory.CreateClientAs(Roles.SuperAdministrator, noMfaStamp: true);
        var page = await legacy.GetAsync("/Admin");
        page.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Headers.Location!.ToString().Should().Contain("reason=mfa-expired");

        // ...but not an unenrolled one: enrolment comes first, so a fresh password-only sign-in is
        // sent to set-up, never bounced to login.
        var unenrolled = _factory.CreateClientAs(Roles.SuperAdministrator, claims: MustEnrol, noMfaStamp: true);
        (await unenrolled.GetAsync("/Admin")).Headers.Location!.ToString().Should().Be("/Account/SetupAuthenticator");
    }

    [Fact]
    public async Task A_recent_code_or_a_role_without_the_requirement_is_left_alone()
    {
        var fresh = _factory.CreateClientAs(Roles.SuperAdministrator, mfaAt: DateTimeOffset.UtcNow.AddDays(-29));
        (await fresh.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, mfaAt: DateTimeOffset.UtcNow.AddDays(-400));
        (await companyAdmin.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
        var unstampedCompanyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, noMfaStamp: true);
        (await unstampedCompanyAdmin.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_login_page_explains_an_expired_session()
    {
        var anonymous = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await anonymous.GetAsync("/Account/Login?reason=mfa-expired");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("30 days since you last verified");
    }
}
