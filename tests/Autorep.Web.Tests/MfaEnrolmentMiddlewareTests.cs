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
}
