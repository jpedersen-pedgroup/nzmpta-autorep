using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Autorep.Web.Tests.E2E.OfflineBrowser;

namespace Autorep.Web.Tests.E2E;

// A Super-Administrator moves a user to another Testing Company from their Users page: a clear
// "Change company" button opens a dialog that says what happens first (tests and farms stay with the
// old company), and only its confirm button moves them. The scope rules themselves are covered by
// MovedTesterTests; this is the dialog in a real browser.
[Trait("Category", "E2E")]
public class ChangeCompanyE2ETests : IClassFixture<AdminEditE2EWebAppFactory>, IAsyncLifetime
{
    private readonly AdminEditE2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public ChangeCompanyE2ETests(AdminEditE2EWebAppFactory factory) => _factory = factory;

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
    public async Task A_super_admin_moves_a_tester_after_reading_what_happens()
    {
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = context;
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("#Input_Email", AdminEditE2EWebAppFactory.AdminEmail);
        await page.FillAsync("#Input_Password", OfflineE2EWebAppFactory.TesterPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => url.Contains("/Account/TwoFactorChallenge"));
        await page.FillAsync("#Input_Code", Totp.Now(_factory.AdminAuthenticatorKey));
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Account/"));

        var before = await CompanyOfAsync(_factory.OtherTesterId);
        await page.GotoAsync($"/Admin/Testers/Edit/{_factory.OtherTesterId}");
        var dialog = page.GetByRole(AriaRole.Dialog);
        Assert.False(await dialog.IsVisibleAsync(), "the dialog waits for the button");

        await page.GetByRole(AriaRole.Button, new() { Name = "Change company" }).ClickAsync();
        await dialog.WaitForAsync();
        await dialog.GetByText($"The farms they've tested stay with {OfflineE2EWebAppFactory.OtherCompanyName} too").WaitForAsync();
        await ShotAsync(page, "c2-change-company-dialog");

        // Cancel leaves everything as it was.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Assert.Equal(before, await CompanyOfAsync(_factory.OtherTesterId));

        await page.GetByRole(AriaRole.Button, new() { Name = "Change company" }).ClickAsync();
        await dialog.GetByLabel("Move to").SelectOptionAsync(new SelectOptionValue { Label = OfflineE2EWebAppFactory.CompanyName });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Change company" }).ClickAsync();

        await page.GetByText($"Moved to {OfflineE2EWebAppFactory.CompanyName}.").WaitForAsync();
        Assert.Equal(_factory.CompanyId, await CompanyOfAsync(_factory.OtherTesterId));
        Assert.Equal(OfflineE2EWebAppFactory.CompanyName, await page.Locator("[data-current-company]").InnerTextAsync());
    }

    // Unsynced tests block a move (Josh, 27 Oct). The tester's device tells the server what it hasn't
    // sent whenever the app is open online; an unfinished test on the server blocks the move too.
    [Fact]
    public async Task A_testers_device_reports_in_and_unfinished_work_blocks_their_move()
    {
        var (testerContext, tester) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using (testerContext)
        {
            await SignInAsync(tester);
            await tester.GotoAsync("/App/Tests");
            DeviceOutbox? report = null;
            for (var i = 0; i < 50 && report is null; i++)
            {
                await Task.Delay(200);
                using var scope = _factory.AppServices.CreateScope();
                report = await scope.ServiceProvider.GetRequiredService<AutorepDbContext>().DeviceOutboxes
                    .AsNoTracking().FirstOrDefaultAsync(d => d.TesterId == _factory.TesterId);
            }
            Assert.NotNull(report);
            Assert.Equal(0, report!.UnsentTests);
            Assert.Contains("Chrome", report.Device);
        }

        // Something they started and haven't signed off, on the server.
        using (var scope = _factory.AppServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.MachineTests.Add(new MachineTest
            {
                TesterId = _factory.TesterId, FarmId = _factory.KowhaiFarmId, TestingCompanyId = _factory.CompanyId,
                ClientId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = context;
        await SignInAsSuperAdminAsync(page);
        await page.GotoAsync($"/Admin/Testers/Edit/{_factory.TesterId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Change company" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.GetByText("can't be moved yet").WaitForAsync();
        await dialog.GetByRole(AriaRole.Link, new() { Name = OfflineE2EWebAppFactory.KowhaiFarm }).WaitForAsync();
        Assert.Equal(0, await dialog.GetByRole(AriaRole.Button, new() { Name = "Change company" }).CountAsync());
        await ShotAsync(page, "c2-move-blocked");
    }

    private async Task SignInAsSuperAdminAsync(IPage page)
    {
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("#Input_Email", AdminEditE2EWebAppFactory.AdminEmail);
        await page.FillAsync("#Input_Password", OfflineE2EWebAppFactory.TesterPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => url.Contains("/Account/TwoFactorChallenge"));
        await page.FillAsync("#Input_Code", Totp.Now(_factory.AdminAuthenticatorKey));
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Account/"));
    }

    private async Task<Guid?> CompanyOfAsync(string userId)
    {
        using var scope = _factory.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.TestingCompanyId).SingleAsync();
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
