using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Boots the app with Bootstrap admin config set (on a fresh InMemory store) so the startup
// BootstrapAdminAsync seed runs, then asserts the account.
public class BootstrapWebAppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "josh@pedersengroup.co.nz";
    public const string AdminPassword = "BootstrapPass123!";
    private readonly string _dbName = "autorep-bootstrap-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.ConfigureTestServices(services =>
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName)));
    }
}

public class BootstrapAdminTests : IClassFixture<BootstrapWebAppFactory>
{
    private readonly BootstrapWebAppFactory _factory;
    public BootstrapAdminTests(BootstrapWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Bootstrap_creates_a_super_admin_with_forced_password_reset()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();

        var admin = await users.FindByEmailAsync(BootstrapWebAppFactory.AdminEmail);

        admin.Should().NotBeNull();
        admin!.ForcedPasswordResetRequired.Should().BeTrue();
        (await users.IsInRoleAsync(admin, Roles.SuperAdministrator)).Should().BeTrue();
    }
}
