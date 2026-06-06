using Autorep.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Like WebAppFactory (InMemory, no SQL Server) but with the TestAuthHandler wired in as the
// default scheme, so integration tests can drive authenticated admin/tester pages. Seeds
// nothing on startup — each test seeds exactly what it needs via Services.CreateScope().
public class AuthedWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "autorep-authtests-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("SeedOnStartup", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName));

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    public HttpClient CreateClientAs(string role, string? userId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (userId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return client;
    }
}
