using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

// Hosts the real app for the offline tester suite: a signed-in TESTER (the admin factory's seed
// can't reach /App), their Testing Company, two farms in the company's book, and one completed test
// the first sync pulls down. Same "build twice" pattern as E2EWebAppFactory — a TestServer host for
// the base class and a Kestrel host the browser hits — but bound to plain HTTP only: every case
// here needs a service worker, and Chromium refuses one on an origin whose certificate it doesn't
// trust (the dev certificate on CI). 127.0.0.1 over HTTP is a secure context.
public class OfflineE2EWebAppFactory : WebApplicationFactory<Program>
{
    private IHost? _kestrelHost;
    private readonly string _dbName = $"autorep-offline-e2e-{Guid.NewGuid()}";

    public string BaseUrl { get; private set; } = "";
    public string TesterId { get; private set; } = "";
    public Guid CompanyId { get; private set; }
    public Guid KowhaiFarmId { get; private set; }
    public Guid RimuFarmId { get; private set; }
    /// <summary>ClientId of the seeded completed test (on Kowhai Flats) the first sync pulls.</summary>
    public Guid CompletedTestClientId { get; private set; }

    public const string TesterEmail = "e2e-tester@local";
    public const string TesterPassword = "E2EPassword123!";
    public const string TesterName = "Ellie Offlinetester";
    public const string CompanyName = "Kahikatea Testing Ltd";
    public const string KowhaiFarm = "Kowhai Flats Dairy";
    public const string RimuFarm = "Rimu Ridge Holdings";

    /// <summary>A second tester, at another company with its own farm — for a device changing hands.</summary>
    public const string OtherTesterEmail = "e2e-other-tester@local";
    public const string OtherCompanyName = "Totara Testing Co";
    public const string MataiFarm = "Matai Meadows";
    public string OtherTesterId { get; private set; } = "";

    /// <summary>A tester whose licence lapsed yesterday: sign-in gives a sync-only session.</summary>
    public const string LapsedTesterEmail = "e2e-lapsed-tester@local";

    /// <summary>
    /// "Signal lost", as the browser experiences it: while set, every request is dropped at the
    /// connection, so a fetch fails with a network error — from the page AND from the service
    /// worker. Playwright's context.SetOfflineAsync only reaches the page in Chromium; the worker's
    /// own fetches sail through it (an experimental driver flag extends it, but only to some worker
    /// sessions, so it can't be relied on). Without this, an "offline" navigation would come back as
    /// a live server page and the suite would never exercise the worker at all.
    /// </summary>
    public bool NetworkDown { get; set; }

    /// <summary>
    /// "A new build has been deployed", as a device notices it: while set, /sw.js is served with this
    /// appended, so the browser sees a byte-different worker and installs it — same cache name, same
    /// files, exactly what a deploy that changes only the worker looks like.
    /// </summary>
    public string? ServiceWorkerSuffix { get; set; }

    /// <summary>
    /// While set, the server answers every sync push with a 503 — "the server won't take it right
    /// now". Tests sync by themselves when online, so this is how a case keeps work unsent on purpose.
    /// </summary>
    public bool RefuseSyncPushes { get; set; }

    /// <summary>
    /// While set, tester pages (GET /App…) take this long to answer — longer than the service
    /// worker's 8 s navigation timeout, so the cached shell answers instead while the API still
    /// works: exactly a slow signal, where the device draws the page and the session check
    /// succeeds behind it.
    /// </summary>
    public int? SlowTesterPagesMs { get; set; }

    /// <summary>The Kestrel host's services — the one the browser talks to, and the only one seeded.</summary>
    public IServiceProvider AppServices => _kestrelHost?.Services
        ?? throw new InvalidOperationException("Host not started — touch Services first.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("SeedOnStartup", "false"); // seeded once, below
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName));
            services.AddSingleton<IStartupFilter>(new NetworkSwitch(this));
        });
    }

    /// <summary>Puts the NetworkDown and ServiceWorkerSuffix switches in front of the whole pipeline.</summary>
    private sealed class NetworkSwitch(OfflineE2EWebAppFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (factory.NetworkDown)
                {
                    context.Abort();
                    return;
                }
                if (factory.SlowTesterPagesMs is { } delay
                    && HttpMethods.IsGet(context.Request.Method)
                    && context.Request.Path.StartsWithSegments("/App"))
                {
                    await Task.Delay(delay);
                }
                if (factory.RefuseSyncPushes
                    && HttpMethods.IsPost(context.Request.Method)
                    && context.Request.Path.StartsWithSegments("/api/sync/tests"))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }
                if (factory.ServiceWorkerSuffix is { } suffix && context.Request.Path == "/sw.js")
                {
                    var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
                    var worker = await File.ReadAllTextAsync(Path.Combine(env.WebRootPath, "sw.js"));
                    context.Response.ContentType = "text/javascript; charset=utf-8";
                    await context.Response.WriteAsync(worker + suffix);
                    return;
                }
                await nextMiddleware();
            });
            next(app);
        };
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();

        builder.ConfigureWebHost(b => b.UseKestrel().UseUrls("http://127.0.0.1:0"));
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        BaseUrl = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        SeedAsync(_kestrelHost.Services).GetAwaiter().GetResult();

        testHost.Start();
        return testHost;
    }

    private async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        await Seed.RolesAsync(sp);
        await Seed.ReferenceDataAsync(sp);

        var db = sp.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = CompanyName };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        CompanyId = company.Id;

        var users = sp.GetRequiredService<UserManager<Tester>>();
        var licence = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        var tester = new Tester
        {
            UserName = TesterEmail,
            Email = TesterEmail,
            EmailConfirmed = true,
            DisplayName = TesterName,
            TestingCompanyId = company.Id,
            LicenceExpiryDate = licence,
            CertificateNo = "E2E-0042",
            // Pre-accepted, so sign-in doesn't divert to /Account/AcceptTerms.
            TermsAcceptedVersion = Seed.DefaultTermsVersion,
            TermsAcceptedAt = DateTimeOffset.UtcNow,
            TermsAcceptedLicenceExpiry = licence,
        };
        var created = await users.CreateAsync(tester, TesterPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(tester, Roles.Tester);
        TesterId = tester.Id;

        // The second tester, in a company of their own.
        var otherCompany = new TestingCompany { Name = OtherCompanyName };
        db.TestingCompanies.Add(otherCompany);
        await db.SaveChangesAsync();
        var other = new Tester
        {
            UserName = OtherTesterEmail, Email = OtherTesterEmail, EmailConfirmed = true,
            DisplayName = "Otto Othertester", TestingCompanyId = otherCompany.Id,
            LicenceExpiryDate = licence, TermsAcceptedVersion = Seed.DefaultTermsVersion,
            TermsAcceptedAt = DateTimeOffset.UtcNow, TermsAcceptedLicenceExpiry = licence,
        };
        await CreateTesterAsync(users, other);
        OtherTesterId = other.Id;
        db.Farms.Add(new Farm { Name = MataiFarm, CreatedByTestingCompanyId = otherCompany.Id });

        // The lapsed tester (same company as the first): licence ran out yesterday.
        var lapsedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await CreateTesterAsync(users, new Tester
        {
            UserName = LapsedTesterEmail, Email = LapsedTesterEmail, EmailConfirmed = true,
            DisplayName = "Lara Lapsed", TestingCompanyId = company.Id,
            LicenceExpiryDate = lapsedOn, TermsAcceptedVersion = Seed.DefaultTermsVersion,
            TermsAcceptedAt = DateTimeOffset.UtcNow, TermsAcceptedLicenceExpiry = lapsedOn,
        });

        // In the company's book (created by it), so /api/farms returns them to this tester.
        var kowhai = new Farm { Name = KowhaiFarm, CreatedByTestingCompanyId = company.Id, SupplyNumber = "40123" };
        var rimu = new Farm { Name = RimuFarm, CreatedByTestingCompanyId = company.Id, SupplyNumber = "40456" };
        db.Farms.AddRange(kowhai, rimu);
        await db.SaveChangesAsync();
        KowhaiFarmId = kowhai.Id;
        RimuFarmId = rimu.Id;

        // A completed test already on the server — the first sync pulls it, and it's what the
        // print-offline case prints. No payload: the pull rehydrates it from the header columns
        // (default configuration), which is all a report needs.
        CompletedTestClientId = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow.AddDays(-2);
        db.MachineTests.Add(new MachineTest
        {
            ClientId = CompletedTestClientId,
            TesterId = tester.Id,
            TestingCompanyId = company.Id,
            FarmId = kowhai.Id,
            CreatedAt = completedAt.AddHours(-3),
            UpdatedAt = completedAt,
            MarkedCompleteAt = completedAt,
        });
        await db.SaveChangesAsync();

        await SeedMoreAsync(sp);
    }

    /// <summary>A derived factory's own seed, run once after everything above, in the same scope.</summary>
    protected virtual Task SeedMoreAsync(IServiceProvider services) => Task.CompletedTask;

    private static async Task CreateTesterAsync(UserManager<Tester> users, Tester tester)
    {
        var created = await users.CreateAsync(tester, TesterPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(tester, Roles.Tester);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _kestrelHost?.Dispose();
    }
}
