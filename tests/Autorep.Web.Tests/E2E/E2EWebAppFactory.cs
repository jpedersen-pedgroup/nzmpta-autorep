using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Autorep.Web.Tests.E2E;

// Hosts the real app on a Kestrel HTTPS port (so a browser can connect) with an InMemory
// store, a stubbed NZ Post backend, and a seeded Super-Administrator + Farm. Uses the
// documented WebApplicationFactory "build twice" pattern: one TestServer host (required by
// the base) and one Kestrel host the browser actually hits.
public class E2EWebAppFactory : WebApplicationFactory<Program>
{
    private IHost? _kestrelHost;
    // Distinct per host build: the base TestServer host and the Kestrel host must NOT share
    // an InMemory store (EF caches its internal provider per identical options), otherwise the
    // startup seed runs twice against one store and duplicates the roles.
    private string _dbName = $"autorep-e2e-{Guid.NewGuid()}";

    public string BaseUrl { get; private set; } = "";
    /// <summary>Same host over plain HTTP. Use this for anything needing a secure context the
    /// dev certificate can't provide on CI — service workers, notably.</summary>
    public string BaseUrlHttp { get; private set; } = "";
    public Guid FarmId { get; private set; }
    public const string AdminEmail = "e2e-admin@local";
    public const string AdminPassword = "E2EPassword123!";
    public const string FarmName = "E2E Test Farm";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("SeedOnStartup", "false"); // this factory seeds once itself
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName));
            services.AddHttpClient<NzPostAddressClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubNzPostHandler());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The base class needs a (TestServer) host built from the builder.
        var testHost = builder.Build();

        // A real Kestrel host on dynamic ports for the browser. HTTP is bound alongside HTTPS
        // because the dev certificate is trusted on a developer machine but NOT on a CI runner,
        // and Chromium refuses to register a service worker on any origin with a certificate
        // error — so a SW test over HTTPS passes locally and hangs forever in CI. localhost is a
        // potentially-trustworthy origin, so plain HTTP gives those tests a working secure context.
        builder.ConfigureWebHost(b => b.UseKestrel().UseUrls("https://127.0.0.1:0", "http://127.0.0.1:0"));
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        var addresses = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        BaseUrl = addresses.Addresses.First(a => a.StartsWith("https:", StringComparison.Ordinal));
        BaseUrlHttp = addresses.Addresses.First(a => a.StartsWith("http:", StringComparison.Ordinal));

        SeedAsync(_kestrelHost.Services).GetAwaiter().GetResult();

        testHost.Start();
        return testHost;
    }

    private async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        // Seed roles + reference data once (Program's startup seed is disabled here),
        // then an admin and a farm for the test to act on.
        await Seed.RolesAsync(sp);
        await Seed.ReferenceDataAsync(sp);

        var users = sp.GetRequiredService<UserManager<Tester>>();
        if (await users.FindByEmailAsync(AdminEmail) is null)
        {
            var admin = new Tester
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                EmailConfirmed = true,
                DisplayName = "E2E Admin"
            };
            await users.CreateAsync(admin, AdminPassword);
            await users.AddToRoleAsync(admin, Roles.SuperAdministrator);
            // Pre-accept the current terms so the login terms-gate doesn't divert the E2E admin
            // to /Account/AcceptTerms (ReferenceDataAsync above seeds the PrivacyContent that
            // activates the gate).
            admin.TermsAcceptedVersion = Seed.DefaultTermsVersion;
            admin.TermsAcceptedAt = DateTimeOffset.UtcNow;
            admin.TermsAcceptedLicenceExpiry = admin.LicenceExpiryDate;
            await users.UpdateAsync(admin);
        }

        var db = sp.GetRequiredService<AutorepDbContext>();
        var farm = await db.Farms.FirstOrDefaultAsync(f => f.Name == FarmName);
        if (farm is null)
        {
            farm = new Farm { Name = FarmName };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
        }
        FarmId = farm.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _kestrelHost?.Dispose();
    }
}
