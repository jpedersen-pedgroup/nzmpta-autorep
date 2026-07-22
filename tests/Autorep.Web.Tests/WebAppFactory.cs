using Autorep.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Boots the real app for integration tests. In the "Testing" environment Program skips the
// SQL Server provider, so the factory supplies a per-run InMemory store. Program detects the
// non-relational provider and EnsureCreated + seeds (roles + reference data) at startup.
public class WebAppFactory : WebApplicationFactory<Program>
{
    // Per-instance store: xUnit builds one fixture per test class, and every instance runs the
    // startup seed. A shared database name would seed reference data once per class and make the
    // seeded-row counts depend on how many test classes happen to use this factory.
    private readonly string _dbName = "autorep-tests-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }
}
