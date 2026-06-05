using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Autorep.Web.Tests.E2E;

// End-to-end happy-path (T5): a real browser drives the admin Farm Details edit + NZ Post
// address autocomplete. NZ Post is stubbed (see StubNzPostHandler) so the run is deterministic.
[Trait("Category", "E2E")]
public class FarmAutocompleteE2ETests : IClassFixture<E2EWebAppFactory>, IAsyncLifetime
{
    private readonly E2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public FarmAutocompleteE2ETests(E2EWebAppFactory factory) => _factory = factory;

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
    public async Task Admin_edits_farm_using_NZ_Post_autocomplete()
    {
        await using var context = await _browser.NewContextAsync(new()
        {
            BaseURL = _factory.BaseUrl,
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();

        // 1. Sign in as the seeded Super-Administrator.
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("#Input_Email", E2EWebAppFactory.AdminEmail);
        await page.FillAsync("#Input_Password", E2EWebAppFactory.AdminPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login"));

        // 2. Open the seeded farm's edit page.
        await page.GotoAsync($"/Admin/Farms/Edit/{_factory.FarmId}");
        await Expect(page.Locator("h1")).ToHaveTextAsync(E2EWebAppFactory.FarmName);

        // 3. Type into the address field and choose the NZ Post suggestion.
        await page.Locator("#Input_AddressLine1").PressSequentiallyAsync("123 Test", new() { Delay = 40 });
        await page.WaitForSelectorAsync("#nzpost-suggestions button");
        await page.Locator("#nzpost-suggestions button").First.ClickAsync();

        // 4. Selecting fills the structured fields from the details endpoint.
        await Expect(page.Locator("#Input_AddressLine1")).ToHaveValueAsync("123 Test Street");
        await Expect(page.Locator("#Input_AddressLine2")).ToHaveValueAsync("Suburbia");
        await Expect(page.Locator("#Input_Town")).ToHaveValueAsync("Testville");
        await Expect(page.Locator("#Input_PostCode")).ToHaveValueAsync("1234");

        // 5. Save and confirm success.
        await page.ClickAsync("button:has-text('Save farm details')");
        await Expect(page.Locator(".alert--success")).ToContainTextAsync("Farm details saved");
    }
}
