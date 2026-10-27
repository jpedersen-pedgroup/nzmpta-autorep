using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Autorep.Web.Tests.E2E.OfflineBrowser;

namespace Autorep.Web.Tests.E2E;

// A Company Administrator is reminded, across the admin portal, about a farm a tester set up in the
// field that has waited for review for over 48 hours, and dismisses the reminder. The rules are in
// FarmReviewReminderTests; this is the banner and its Dismiss in a real browser.
[Trait("Category", "E2E")]
public class FarmReviewBannerE2ETests : IClassFixture<AdminEditE2EWebAppFactory>, IAsyncLifetime
{
    private readonly AdminEditE2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public FarmReviewBannerE2ETests(AdminEditE2EWebAppFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _ = _factory.Services; // force host start + seed
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    [Fact]
    public async Task A_company_admin_is_reminded_about_a_farm_waiting_48_hours_and_dismisses_it()
    {
        const string farmName = "Harakeke Holdings";
        using (var scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.Farms.Add(new Farm
            {
                Name = farmName,
                CreatedByTestingCompanyId = _factory.CompanyId,
                CreatedByTesterId = _factory.TesterId,
                PendingReviewSince = DateTimeOffset.UtcNow.AddDays(-3),
            });
            await db.SaveChangesAsync();
        }

        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = context;
        await SignInAsync(page, OfflineE2EWebAppFactory.CompanyAdminEmail, landsOn: "/Admin");

        await page.GotoAsync("/Admin/Tests");
        var banner = page.Locator("[data-farm-review-banner]");
        await banner.GetByRole(AriaRole.Link, new() { Name = farmName }).WaitForAsync();
        await banner.GetByText($"added by {OfflineE2EWebAppFactory.TesterName}").WaitForAsync();
        await ShotAsync(page, "c3-farm-review-banner");

        await banner.GetByRole(AriaRole.Button, new() { Name = $"Dismiss the reminder about {farmName}" }).ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/Admin/Tests"));
        Assert.Equal(0, await page.Locator("[data-farm-review-banner]").CountAsync());

        // Still awaiting review on the farms list (its own alert is unchanged).
        await page.GotoAsync("/Admin/Farms");
        await page.GetByText("awaiting review").First.WaitForAsync();
        Assert.Equal(0, await page.Locator("[data-farm-review-banner]").CountAsync());
    }

    private static async Task ShotAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("E2E_SCREENSHOTS") is { Length: > 0 } dir)
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(dir, $"{name}.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled,
            });
    }
}
