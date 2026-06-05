using System.Net;
using Autorep.Web.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

public class IntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public IntegrationTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var res = await _factory.CreateClient().GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_farms_redirects_anonymous_to_login()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/Admin/Farms");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Address_proxy_rejects_anonymous()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/api/address/suggest?q=victoria");
        ((int)res.StatusCode).Should().BeOneOf(302, 401);
    }

    [Fact]
    public void Reference_data_is_seeded_on_startup()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        db.Regions.Count().Should().Be(16);
        db.MilkSupplyCompanies.Count().Should().BeGreaterThanOrEqualTo(10);
    }
}
