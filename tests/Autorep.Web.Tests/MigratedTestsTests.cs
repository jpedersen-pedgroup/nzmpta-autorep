using System.Net;
using Autorep.Web.Domain;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Autorep.Web.Tests;

// The migrated-test banner's cutoff date ("Migrated from AutoRep Plus — tests before {date}"): one
// app setting, handed to the wizard's pages for the client (Client/wizard/migrated.ts).
public class MigratedTestsTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public MigratedTestsTests(AuthedWebAppFactory factory) => _factory = factory;

    private static IConfiguration Config(string? cutoff) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [MigratedTests.CutoffSetting] = cutoff })
        .Build();

    [Theory]
    [InlineData("2026-12-01", "2026-12-01")]
    [InlineData(" 2026-12-01 ", "2026-12-01")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("1/12/2026", "")]
    [InlineData("TBC", "")]
    public void Cutoff_is_a_yyyy_mm_dd_date_or_unset(string? setting, string attribute)
    {
        MigratedTests.CutoffAttribute(Config(setting)).Should().Be(attribute);
    }

    [Fact]
    public async Task Admin_viewer_page_carries_the_cutoff_for_the_client()
    {
        var client = _factory
            .WithWebHostBuilder(b => b.UseSetting(MigratedTests.CutoffSetting, "2026-12-01"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, Roles.SuperAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "migrated-banner-super");

        var res = await client.GetAsync($"/Admin/Tests/View/{Guid.NewGuid()}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("data-migrated-cutoff=\"2026-12-01\"");
    }

    [Fact]
    public async Task Admin_viewer_page_sends_an_empty_cutoff_while_it_is_unset()
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator, "migrated-banner-super");

        var html = await client.GetStringAsync($"/Admin/Tests/View/{Guid.NewGuid()}");

        // Present but empty: the device forgets any date it remembered (an absent attribute means
        // the offline shell, which falls back to that remembered date).
        html.Should().Contain("data-migrated-cutoff=\"\"");
    }
}
