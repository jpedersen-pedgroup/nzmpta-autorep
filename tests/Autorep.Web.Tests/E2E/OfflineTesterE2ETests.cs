using System.Text.Json;
using Autorep.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Autorep.Web.Tests.E2E.OfflineBrowser;

namespace Autorep.Web.Tests.E2E;

// The offline tester app (plans/offline-tester-app.md §7), driven in a real browser. Each case goes
// online first — sign in, let the service worker take control and precache, let the device write its
// identity record — then cuts the network (GoOfflineAsync: page AND service worker) and works without it.
[Trait("Category", "E2E")]
public class OfflineTesterE2ETests : IClassFixture<OfflineE2EWebAppFactory>, IAsyncLifetime
{
    private readonly OfflineE2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public OfflineTesterE2ETests(OfflineE2EWebAppFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _ = _factory.Services; // force host start + seed
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        _factory.NetworkDown = false;
        _factory.ServiceWorkerSuffix = null;
        _factory.RefuseSyncPushes = false;
        _factory.SlowTesterPagesMs = null;
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    /// <summary>
    /// No signal: the page's own view (navigator.onLine false, its fetches fail — Playwright's
    /// offline emulation) AND the service worker's (the host drops every connection, see
    /// OfflineE2EWebAppFactory.NetworkDown). Cases in a class run one at a time, so the shared
    /// switch is safe; each case starts back online.
    /// </summary>
    private async Task GoOfflineAsync(IBrowserContext context)
    {
        await context.SetOfflineAsync(true);
        _factory.NetworkDown = true;
    }

    private async Task GoOnlineAsync(IBrowserContext context)
    {
        _factory.NetworkDown = false;
        await context.SetOfflineAsync(false);
    }

    private async Task<(IBrowserContext Context, IPage Page)> OnlineAndReadyAsync()
    {
        _factory.NetworkDown = false;
        _factory.RefuseSyncPushes = false;
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await SignInAsync(page);
        await WaitUntilReadyForOfflineAsync(page);
        return (context, page);
    }

    /// <summary>Online "Sync now" on My tests, until the seeded completed test is listed.</summary>
    private static async Task SyncNowAsync(IPage page)
    {
        await page.GotoAsync("/App/Tests");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        await page.Locator("tr", new() { HasText = OfflineE2EWebAppFactory.KowhaiFarm })
            .GetByText("Complete").WaitForAsync();
    }

    private static ILocator Step(IPage page, string title) =>
        page.Locator(".wizard__step", new() { HasText = title });

    [Fact]
    public async Task Cold_launch_offline_lands_on_the_testers_home_drawn_from_the_device()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;

        await GoOfflineAsync(context);
        // The installed app's start_url: a server-side role redirect online, the shell offline.
        await page.GotoAsync("/");

        await page.GetByText("Welcome back").WaitForAsync();
        Assert.True(await IsShellAsync(page), "the home page should have come from the cached shell");
        Assert.EndsWith("/App", page.Url);
        // The header the shell drew from the identity record — the document itself names no one.
        Assert.Equal(OfflineE2EWebAppFactory.TesterEmail, await page.Locator("#shell-user-name").TextContentAsync());
        await page.Locator(".app-status[data-connection=offline]").WaitForAsync();
        foreach (var nav in new[] { "Home", "My tests", "New test" })
        {
            Assert.True(await page.Locator(".app-header__nav").GetByRole(AriaRole.Link, new() { Name = nav, Exact = true }).IsVisibleAsync(),
                $"the '{nav}' link is missing from the shell's header");
        }
    }

    [Fact]
    public async Task Offline_the_tester_gets_from_home_to_my_tests_to_a_saved_test()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await SyncNowAsync(page);

        await GoOfflineAsync(context);
        await page.GotoAsync("/App");
        await page.Locator(".tile", new() { HasText = "My tests" }).ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/App/Tests", StringComparison.Ordinal));
        Assert.True(await IsShellAsync(page));

        var row = page.Locator("tr", new() { HasText = OfflineE2EWebAppFactory.KowhaiFarm });
        await row.GetByRole(AriaRole.Link, new() { Name = "View" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/App/Tests/Wizard?id=", StringComparison.Ordinal));
        Assert.True(await IsShellAsync(page));
        await page.GetByText("Offline — saved on device").WaitForAsync();
        await Step(page, "Review & Sign-Off").WaitForAsync();
    }

    [Fact]
    public async Task A_test_captured_offline_is_saved_on_the_device_and_survives_a_reload()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        // The farm book arrives in the background after the first tester page loads.
        Assert.True(await PollAsync(page, @"async ([testerId, farm]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('reference')) { db.close(); resolve(false); return; }
                    const get = db.transaction('reference').objectStore('reference').get('farms');
                    get.onsuccess = () => { db.close(); resolve((get.result?.rows ?? []).some((f) => f.name === farm)); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.TesterId, OfflineE2EWebAppFactory.RimuFarm }), "the farm book never reached the device");

        await GoOfflineAsync(context);
        await page.GotoAsync($"/App/Tests/Wizard?farmId={_factory.RimuFarmId}&farmName={Uri.EscapeDataString(OfflineE2EWebAppFactory.RimuFarm)}");
        await page.WaitForURLAsync(url => url.Contains("?id=", StringComparison.Ordinal));
        Assert.True(await IsShellAsync(page));
        var testId = new Uri(page.Url).Query.Split("id=")[1];

        await Step(page, "Machine Configuration & Ancillary").ClickAsync();
        await page.FillAsync("input[placeholder='e.g. 30 a-side']", "24 a-side");

        Assert.True(await PollAsync(page, @"async ([testerId, testId]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('tests').objectStore('tests').get(testId);
                    get.onsuccess = () => { db.close(); resolve(get.result?.config?.plantSize === '24 a-side'); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.TesterId, testId }), "the edit never reached IndexedDB");

        var stored = JsonDocument.Parse((await LocalTestJsonAsync(page, _factory.TesterId, testId))!).RootElement;
        Assert.Equal("local-only", stored.GetProperty("syncState").GetString());
        // The farm's details came from the cached farm book — there was no server to ask.
        Assert.Equal(OfflineE2EWebAppFactory.RimuFarm, stored.GetProperty("farm").GetProperty("name").GetString());
        await page.Locator(".app-status[data-connection=offline]", new() { HasText = "1 unsent" }).WaitForAsync();

        await page.ReloadAsync();
        Assert.True(await IsShellAsync(page));
        await page.Locator("input[placeholder='e.g. 30 a-side']").WaitForAsync();
        Assert.Equal("24 a-side", await page.InputValueAsync("input[placeholder='e.g. 30 a-side']"));
    }

    /// <summary>Online, until the farm book holds <paramref name="farm"/> (it arrives in the background).</summary>
    private async Task WaitForFarmBookAsync(IPage page, string farm) =>
        Assert.True(await PollAsync(page, @"async ([testerId, farm]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('reference')) { db.close(); resolve(false); return; }
                    const get = db.transaction('reference').objectStore('reference').get('farms');
                    get.onsuccess = () => { db.close(); resolve((get.result?.rows ?? []).some((f) => f.name === farm)); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.TesterId, farm }), $"the farm book never brought {farm} to the device");

    // Phase 3: the New-test page is drawn from the farm book on the device, so a test can start
    // at a farm with no signal at all — the whole point of an offline tester app.
    [Fact]
    public async Task A_test_starts_offline_from_the_farm_book_on_the_device()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await WaitForFarmBookAsync(page, OfflineE2EWebAppFactory.RimuFarm);

        await GoOfflineAsync(context);
        await page.GotoAsync("/App");
        await page.Locator(".tile", new() { HasText = "Start a new test" }).ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/App/Tests/New", StringComparison.Ordinal));
        Assert.True(await IsShellAsync(page));

        await page.FillAsync("#farm-search", "rimu");
        await page.Locator("#farm-menu button", new() { HasText = OfflineE2EWebAppFactory.RimuFarm }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Start test" }).ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("/App/Tests/Wizard?id=", StringComparison.Ordinal));
        var testId = new Uri(page.Url).Query.Split("id=")[1];
        Assert.True(await PollAsync(page, @"async ([testerId, testId]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('tests').objectStore('tests').get(testId);
                    get.onsuccess = () => { db.close(); resolve(!!get.result?.farm?.supplyNumber); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.TesterId, testId }), "the wizard never snapshotted the farm from the cached book");

        var stored = JsonDocument.Parse((await LocalTestJsonAsync(page, _factory.TesterId, testId))!).RootElement;
        Assert.Equal(_factory.RimuFarmId.ToString(), stored.GetProperty("farmId").GetString());
        Assert.Equal("40456", stored.GetProperty("farm").GetProperty("supplyNumber").GetString());
        Assert.Equal("local-only", stored.GetProperty("syncState").GetString());
    }

    // Adding a farm stays online-only (Phase 5 was cut). Offline it must say so plainly instead of
    // failing silently; online it adds the farm and the device can pick it from then on.
    [Fact]
    public async Task Adding_a_farm_needs_a_connection_and_then_lands_in_the_farm_book()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await WaitForFarmBookAsync(page, OfflineE2EWebAppFactory.RimuFarm);

        await GoOfflineAsync(context);
        await page.GotoAsync("/App/Tests/New");
        await page.GetByRole(AriaRole.Button, new() { Name = "＋ Add a new farm" }).ClickAsync();
        await page.GetByText("Adding a farm needs a connection").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();

        await GoOnlineAsync(context);
        await page.GotoAsync("/App/Tests/New");
        Assert.False(await IsShellAsync(page));
        await page.GetByRole(AriaRole.Button, new() { Name = "＋ Add a new farm" }).ClickAsync();
        await page.FillAsync("#nf-name", "Totara Valley E2E");
        await page.FillAsync("#nf-supply", "70777");
        await page.ClickAsync("#nf-create");

        // Picked straight away, and in the cached book for next time — offline included.
        await Assertions.Expect(page.Locator("#farm-search")).ToHaveValueAsync("Totara Valley E2E");
        await WaitForFarmBookAsync(page, "Totara Valley E2E");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start test" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/App/Tests/Wizard?id=", StringComparison.Ordinal));
    }

    // PRD story 34: a test captured with no signal goes up by itself once the signal is back —
    // nobody has to remember Sync now. The tester does nothing between "offline" and "on the server".
    [Fact]
    public async Task A_test_captured_offline_goes_up_by_itself_when_the_connection_returns()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await WaitForFarmBookAsync(page, OfflineE2EWebAppFactory.RimuFarm);

        await GoOfflineAsync(context);
        await page.GotoAsync($"/App/Tests/Wizard?farmId={_factory.RimuFarmId}&farmName={Uri.EscapeDataString(OfflineE2EWebAppFactory.RimuFarm)}");
        await page.WaitForURLAsync(url => url.Contains("?id=", StringComparison.Ordinal));
        var testId = new Uri(page.Url).Query.Split("id=")[1];
        await Step(page, "Machine Configuration & Ancillary").ClickAsync();
        await page.FillAsync("input[placeholder='e.g. 30 a-side']", "18 a-side");
        await page.Locator(".app-status[data-connection=offline]", new() { HasText = "1 unsent" }).WaitForAsync();

        await GoOnlineAsync(context);

        Assert.True(await PollAsync(page, @"async ([testerId, testId]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('tests').objectStore('tests').get(testId);
                    get.onsuccess = () => { db.close(); resolve(get.result?.syncState === 'uploaded'); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.TesterId, testId }), "the test never synced by itself after reconnecting");

        using var scope = _factory.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var stored = await db.MachineTests.SingleAsync(t => t.ClientId == Guid.Parse(testId));
        Assert.Equal(_factory.TesterId, stored.TesterId);
        Assert.Contains("18 a-side", stored.PayloadJson);
        await page.Locator(".app-status[data-connection=online]", new() { HasText = "Online" }).WaitForAsync();
    }

    [Fact]
    public async Task A_report_prints_offline_after_one_online_sync()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await SyncNowAsync(page);
        // A successful sync warms the report generator's lazy chunks (generatorChunks.ts).
        Assert.True(await PollAsync(page, @"async () => {
                const keys = [];
                for (const name of await caches.keys()) {
                    for (const r of await (await caches.open(name)).keys()) keys.push(new URL(r.url).pathname);
                }
                return ['/chunks/pdfmake-', '/chunks/vfs_fonts-', '/chunks/es-'].every((c) => keys.some((k) => k.includes(c)));
            }", timeoutMs: 60_000), "the report generator was never warmed after the sync");

        await GoOfflineAsync(context);
        await page.GotoAsync("/App/Tests");
        await page.Locator("tr", new() { HasText = OfflineE2EWebAppFactory.KowhaiFarm })
            .GetByRole(AriaRole.Link, new() { Name = "View" }).ClickAsync();
        await Step(page, "Review & Sign-Off").ClickAsync();

        var download = await page.RunAndWaitForDownloadAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Download report (PDF)" }).ClickAsync(),
            new() { Timeout = 60_000 });

        Assert.EndsWith(".pdf", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
        var path = await download.PathAsync();
        var head = new byte[5];
        await using (var file = File.OpenRead(path))
        {
            Assert.True(file.Length > 1_000, $"the PDF is suspiciously small ({file.Length} bytes)");
            await file.ReadExactlyAsync(head);
        }
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
    }

    // The test the whole shell strategy rests on (§5d): the Cache API is shared by every account on
    // the device, so after the app has been used every way it gets used — online and offline, every
    // tester page, a sync, a test opened — nothing in ANY cache may name the tester or a farm.
    [Fact]
    public async Task Nothing_that_identifies_a_tester_or_a_farm_lands_in_the_Cache_API()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        await SyncNowAsync(page);
        foreach (var path in new[] { "/App", "/App/Tests/Company", "/App/Tests/New", "/Account/Logout" })
            await page.GotoAsync(path);
        await page.GotoAsync($"/App/Tests/Wizard?id={_factory.CompletedTestClientId}");
        await page.GetByText(OfflineE2EWebAppFactory.KowhaiFarm).First.WaitForAsync();

        await GoOfflineAsync(context);
        foreach (var path in new[] { "/", "/App/Tests", $"/App/Tests/Wizard?id={_factory.CompletedTestClientId}", "/App/Tests/New" })
        {
            await page.GotoAsync(path);
            Assert.True(await IsShellAsync(page), $"{path} should have come from the shell");
        }

        var needles = new[]
        {
            OfflineE2EWebAppFactory.TesterName, OfflineE2EWebAppFactory.TesterEmail, _factory.TesterId,
            OfflineE2EWebAppFactory.CompanyName, OfflineE2EWebAppFactory.KowhaiFarm, OfflineE2EWebAppFactory.RimuFarm,
        };
        var json = await page.EvaluateAsync<string>(@"async (needles) => {
                const hits = [];
                let scanned = 0;
                for (const name of await caches.keys()) {
                    const cache = await caches.open(name);
                    for (const request of await cache.keys()) {
                        scanned++;
                        const body = await (await cache.match(request)).text().catch(() => '');
                        for (const needle of needles) {
                            if (body.includes(needle) || decodeURIComponent(request.url).includes(needle))
                                hits.push(name + ' ' + request.url + ' contains ' + needle);
                        }
                    }
                }
                return JSON.stringify({ scanned, hits });
            }", needles);
        var result = JsonDocument.Parse(json).RootElement;

        Assert.True(result.GetProperty("scanned").GetInt32() > 10, $"too few cache entries to mean anything: {json}");
        Assert.Empty(result.GetProperty("hits").EnumerateArray().Select(h => h.GetString()));
    }

    // A deploy replaces the bundle; a device must not be left running a cached entry whose chunks are
    // gone, and must not reload itself in a loop trying to fix it (§4 Phase 1, §5c).
    [Fact]
    public async Task A_cached_bundle_whose_chunks_were_deployed_away_recovers_without_a_reload_loop()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;

        // The previous build, as far as this device knows: its entry imports a chunk the server
        // (the current deploy) no longer has.
        await page.EvaluateAsync(@"async () => {
                const name = (await caches.keys()).find((k) => /^autorep-[0-9a-f]{12}$/.test(k));
                const cache = await caches.open(name);
                await cache.put(new Request('/js/dist/autorep.js'), new Response(
                    'import ""/js/dist/chunks/chunk-DEPLOYEDAWAY.js"";',
                    { headers: { 'Content-Type': 'text/javascript' } }));
            }");

        // A tablet launching the app has nothing in the renderer's memory cache, so every script
        // request reaches the service worker. This tab loaded the real bundle a moment ago, and
        // Chromium would hand that copy straight back without asking the worker — which is not the
        // situation under test. Switch the renderer's caches off; the worker's Cache API is unaffected.
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });

        var navigations = 0;
        var console = new List<string>();
        page.FrameNavigated += (_, frame) => { if (frame == page.MainFrame) navigations++; };
        page.Console += (_, msg) => console.Add($"{msg.Type}: {msg.Text}");
        page.PageError += (_, error) => console.Add($"pageerror: {error}");
        await page.GotoAsync("/App/Tests");

        Assert.True(await PollAsync(page, "async () => !!document.querySelector('#test-list-root .page-header')"),
            "the page never recovered — the stale bundle is still in charge. Navigations: " + navigations +
            ". Problem bar: " + await page.EvaluateAsync<string?>("() => document.getElementById('app-load-problem')?.textContent ?? null") +
            ". Console: " + string.Join(" | ", console));
        Assert.True(await PollAsync(page, @"async () => {
                const hit = await caches.match('/js/dist/autorep.js', { ignoreSearch: true });
                return !!hit && !(await hit.text()).includes('DEPLOYEDAWAY');
            }"), "the cache still holds the stale bundle");

        var settled = navigations;
        await Task.Delay(3_000);
        Assert.Equal(settled, navigations);
        // The first load, then exactly one recovery reload — not zero (nothing was fixed), not a loop.
        Assert.Equal(2, navigations);
    }

    // Offline = one tester per device (§5a): signing out is the moment the device forgets who was
    // here, so the next person to pick the iPad up offline can't land in this tester's tests. It
    // warns about work still on the device, and never deletes it.
    [Fact]
    public async Task Signing_out_warns_about_unsent_work_keeps_it_and_forgets_who_was_signed_in()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        // Online, a test now sends itself; the server refusing it is what leaves work unsent here.
        _factory.RefuseSyncPushes = true;
        await page.GotoAsync($"/App/Tests/Wizard?farmId={_factory.RimuFarmId}&farmName={Uri.EscapeDataString(OfflineE2EWebAppFactory.RimuFarm)}");
        await page.WaitForURLAsync(url => url.Contains("?id=", StringComparison.Ordinal));
        var testId = new Uri(page.Url).Query.Split("id=")[1];

        string? warning = null;
        page.Dialog += async (_, dialog) =>
        {
            warning = dialog.Message;
            await dialog.AcceptAsync();
        };
        await page.GotoAsync("/App");
        await page.Locator(".app-header__user button", new() { HasText = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/Account/Login", StringComparison.Ordinal));

        Assert.NotNull(warning);
        Assert.Contains("1 test is still only on this device", warning);
        Assert.True(await PollAsync(page, @"async () => !(await indexedDB.databases()).some((d) => d.name === 'autorep-identity')"),
            "the identity record survived sign-out");
        Assert.NotNull(await LocalTestJsonAsync(page, _factory.TesterId, testId));

        await GoOfflineAsync(context);
        await page.GotoAsync("/");
        Assert.True(await IsShellAsync(page));
        await page.GetByText("nobody is signed in here").WaitForAsync();
        Assert.Equal(0, await page.Locator("#test-list-root, #wizard-root, #home-root").CountAsync());
    }

    // Straight after a deploy, a page drawn by the bundle (here New test, which the previous build
    // couldn't draw) can arrive before the new worker has taken over, so the device's cached bundle
    // is the old one and the page sits empty. When the new build arrives, a page with nothing drawn
    // has nothing to lose: it must reload into the new build by itself, once.
    [Fact]
    public async Task A_page_the_old_bundle_cannot_draw_reloads_into_the_new_build_by_itself()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        try
        {
            // The previous build, as far as this page is concerned: it loads fine and draws nothing.
            await page.EvaluateAsync(@"async () => {
                    const name = (await caches.keys()).find((k) => /^autorep-[0-9a-f]{12}$/.test(k));
                    await (await caches.open(name)).put(new Request('/js/dist/autorep.js'), new Response(
                        '/* the previous build: knows nothing about this page */',
                        { headers: { 'Content-Type': 'text/javascript' } }));
                }");
            var cdp = await context.NewCDPSessionAsync(page); // a real launch has no renderer memory cache
            await cdp.SendAsync("Network.enable");
            await cdp.SendAsync("Network.setCacheDisabled", new Dictionary<string, object> { ["cacheDisabled"] = true });

            var navigations = 0;
            page.FrameNavigated += (_, frame) => { if (frame == page.MainFrame) navigations++; };
            await page.GotoAsync("/App/Tests/New");
            Assert.Equal(0, await page.Locator("#new-test-root > *").CountAsync());

            // Deploy: the worker changes, the browser installs it, and it takes over.
            _factory.ServiceWorkerSuffix = $"\n// deployed {Guid.NewGuid()}\n";
            await page.EvaluateAsync("() => navigator.serviceWorker.getRegistration().then((r) => r.update())");

            await page.Locator("#farm-search").WaitForAsync();
            Assert.Equal(2, navigations);
        }
        finally
        {
            _factory.ServiceWorkerSuffix = null;
        }
    }

    // A lapsed licence lands on /Account/FinishSync, which isn't a tester page. The device must still
    // learn the session is sync-only, or an offline launch would reopen the full tester app from an
    // identity record written while the licence was current.
    [Fact]
    public async Task A_lapsed_licence_stays_sync_only_offline()
    {
        _factory.NetworkDown = false;
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = context;
        await SignInAsync(page, OfflineE2EWebAppFactory.LapsedTesterEmail, landsOn: "/Account/FinishSync");
        await WaitUntilReadyForOfflineAsync(page);
        Assert.True(await PollAsync(page, @"async () => new Promise((resolve) => {
                const req = indexedDB.open('autorep-identity');
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('identity').objectStore('identity').get('current');
                    get.onsuccess = () => { db.close(); resolve(get.result?.syncOnly === true); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })"), "the device never learned the session is sync-only");

        await GoOfflineAsync(context);
        foreach (var path in new[] { "/", "/App/Tests/New" })
        {
            await page.GotoAsync(path);
            Assert.True(await IsShellAsync(page));
            await page.GetByText("Your licence has expired").WaitForAsync();
            Assert.Equal(0, await page.Locator("#home-root, #new-test-root, #test-list-root, #wizard-root").CountAsync());
        }
    }

    // The shell draws whoever the device's identity record names, and the cookie may belong to
    // someone else by then — tester A's sign-in lapsed without a sign-out, and tester B signs in on
    // a slow signal. Nothing of B's may be written into A's store, and none of A's unsent work may be
    // sent as B's: a test is signed off by a certified tester, and the server files it under whoever
    // is signed in.
    [Fact]
    public async Task A_device_changing_hands_never_mixes_two_testers_data()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;
        _factory.RefuseSyncPushes = true; // keep A's test unsent while A is signed in
        await page.GotoAsync($"/App/Tests/Wizard?farmId={_factory.RimuFarmId}&farmName={Uri.EscapeDataString(OfflineE2EWebAppFactory.RimuFarm)}");
        await page.WaitForURLAsync(url => url.Contains("?id=", StringComparison.Ordinal));
        var aTestId = new Uri(page.Url).Query.Split("id=")[1];
        _factory.RefuseSyncPushes = false; // from here on, a push WOULD land

        await context.ClearCookiesAsync(); // A's session is gone; the device still names A
        _factory.SlowTesterPagesMs = 10_000; // longer than the worker's 8 s: the shell answers
        await SignInAsync(page, OfflineE2EWebAppFactory.OtherTesterEmail);

        // The shell opens as A, the session check says B, and the page starts again as B.
        Assert.True(await PollAsync(page,
            $"async () => document.getElementById('shell-user-name')?.textContent === '{OfflineE2EWebAppFactory.OtherTesterEmail}'",
            timeoutMs: 60_000), "the shell never came back as the tester actually signed in");
        Assert.True(await PollAsync(page, @"async ([testerId, farm]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('reference')) { db.close(); resolve(false); return; }
                    const get = db.transaction('reference').objectStore('reference').get('farms');
                    get.onsuccess = () => { db.close(); resolve((get.result?.rows ?? []).some((f) => f.name === farm)); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { _factory.OtherTesterId, OfflineE2EWebAppFactory.MataiFarm }, 30_000),
            "B's farm book never reached B's own store");

        Assert.False(await FarmBookHasAsync(page, _factory.TesterId, OfflineE2EWebAppFactory.MataiFarm),
            "B's farm book was written into A's store");
        Assert.NotNull(await LocalTestJsonAsync(page, _factory.TesterId, aTestId));
        using var scope = _factory.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        Assert.False(await db.MachineTests.AnyAsync(t => t.ClientId == Guid.Parse(aTestId)),
            "A's unsent test was sent while B was signed in");
    }

    [Fact]
    public async Task Admin_and_account_pages_are_never_drawn_from_the_shell()
    {
        var (context, page) = await OnlineAndReadyAsync();
        await using var _ = context;

        await GoOfflineAsync(context);
        foreach (var path in new[] { "/Admin", "/Admin/Tests", "/Account/Manage" })
        {
            await page.GotoAsync(path);
            Assert.False(await IsShellAsync(page), $"{path} must not be served from the shell");
            Assert.Equal("Offline · NZMPTA AutoRep", await page.TitleAsync());
        }
    }
}
