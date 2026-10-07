using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Autorep.Web.Tests.E2E.OfflineBrowser;

namespace Autorep.Web.Tests.E2E;

// The Final Report as signed off, end to end in a real browser: the tester signs off, their device
// sends the full report behind the test, and an administrator downloads that exact copy. Offline,
// the report waits on the device and follows the test up once the signal is back.
[Trait("Category", "E2E")]
public class FinalReportE2ETests : IClassFixture<OfflineE2EWebAppFactory>, IAsyncLifetime
{
    private readonly OfflineE2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public FinalReportE2ETests(OfflineE2EWebAppFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _ = _factory.Services; // force host start + seed
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        _factory.NetworkDown = false;
        _factory.RefuseSyncPushes = false;
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    private async Task<(IBrowserContext Context, IPage Page)> TesterOnlineAndReadyAsync()
    {
        _factory.NetworkDown = false;
        _factory.RefuseSyncPushes = false;
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await SignInAsync(page);
        await WaitUntilReadyForOfflineAsync(page);
        return (context, page);
    }

    /// <summary>Starts a test at Rimu Ridge and returns its client id.</summary>
    private async Task<string> StartTestAsync(IPage page)
    {
        await page.GotoAsync($"/App/Tests/Wizard?farmId={_factory.RimuFarmId}&farmName={Uri.EscapeDataString(OfflineE2EWebAppFactory.RimuFarm)}");
        await page.WaitForURLAsync(url => url.Contains("?id=", StringComparison.Ordinal));
        return new Uri(page.Url).Query.Split("id=")[1];
    }

    private static async Task SignOffAsync(IPage page)
    {
        await page.Locator(".wizard__step", new() { HasText = "Review & Sign-Off" }).ClickAsync();
        await page.GetByLabel("I confirm this test has been completed and the results are accurate.").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Mark as complete & sync" }).ClickAsync();
        await page.GetByText("✓ Completed").WaitForAsync();
    }

    /// <summary>The server's record of the stored report for a test, once there is one.</summary>
    private async Task<(Guid TestId, FinalReportBlob Record)?> StoredReportAsync(string clientId, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var id = Guid.Parse(clientId);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.AppServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var test = await db.MachineTests.AsNoTracking().FirstOrDefaultAsync(t => t.ClientId == id && t.TesterId == _factory.TesterId);
            var record = test is null ? null : await db.FinalReportBlobs.AsNoTracking().FirstOrDefaultAsync(r => r.MachineTestId == test.Id);
            if (record is not null) return (test!.Id, record);
            await Task.Delay(250);
        }
        return null;
    }

    private byte[] StoredBytes(FinalReportBlob record) =>
        _factory.AppServices.GetRequiredService<InMemoryPdfStore>()
            .GetAsync(PdfContainer.FinalReports, record.BlobKey).GetAwaiter().GetResult()!.Bytes;

    /// <summary>Waits until the device's upload queue does (or doesn't) hold the test's report.</summary>
    private static Task<bool> QueuedOnDeviceAsync(IPage page, string testerId, string testId, bool queued) =>
        PollAsync(page, @"async ([testerId, testId, queued]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('reference').objectStore('reference').get('finalReport:' + testId);
                    get.onsuccess = () => { db.close(); resolve(!!get.result === queued); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new object[] { testerId, testId, queued });

    /// <summary>Every cache on the device, as "cache url" lines, with anything that names the test's
    /// farm or tester or holds a PDF that isn't a work-instruction guide.</summary>
    private static async Task<List<string>> CacheProblemsAsync(IPage page, string[] needles)
    {
        var json = await page.EvaluateAsync<string>(@"async (needles) => {
                const hits = [];
                for (const name of await caches.keys()) {
                    const cache = await caches.open(name);
                    for (const request of await cache.keys()) {
                        const response = await cache.match(request);
                        const type = response.headers.get('content-type') || '';
                        const url = decodeURIComponent(request.url);
                        if (type.includes('pdf') && !new URL(request.url).pathname.startsWith('/guides/')) hits.push(name + ' ' + url + ' is a PDF');
                        if (url.includes('final-report') || url.includes('pulsation-pdf')) hits.push(name + ' ' + url + ' is a report route');
                        const body = await response.clone().text().catch(() => '');
                        for (const needle of needles) {
                            if (body.includes(needle) || url.includes(needle)) hits.push(name + ' ' + url + ' contains ' + needle);
                        }
                    }
                }
                return JSON.stringify(hits);
            }", needles);
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    [Fact]
    public async Task A_signed_off_report_is_stored_and_an_admin_downloads_that_exact_copy()
    {
        var (context, page) = await TesterOnlineAndReadyAsync();
        await using var _ = context;
        var testId = await StartTestAsync(page);

        await SignOffAsync(page);

        var stored = await StoredReportAsync(testId);
        Assert.True(stored is not null, "the report as signed off never reached the server");
        var bytes = StoredBytes(stored!.Value.Record);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(PdfHash.Sha256Hex(bytes), stored.Value.Record.Sha256);
        Assert.True(await QueuedOnDeviceAsync(page, _factory.TesterId, testId, queued: false),
            "the device kept the report queued after the server took it");
        Assert.Empty(await CacheProblemsAsync(page, [OfflineE2EWebAppFactory.TesterName, OfflineE2EWebAppFactory.RimuFarm]));

        // The admin side: a Company Administrator at the tester's company opens the test.
        var (adminContext, admin) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var __ = adminContext;
        await SignInAsync(admin, OfflineE2EWebAppFactory.CompanyAdminEmail, landsOn: "/Admin");
        await admin.GotoAsync($"/Admin/Tests/View/{stored.Value.TestId}");
        await admin.Locator(".wizard__step", new() { HasText = "Review & Sign-Off" }).ClickAsync();
        await admin.Locator("[data-stored-report]", new() { HasText = "copy the tester's device made at sign-off" }).WaitForAsync();

        var download = await admin.RunAndWaitForDownloadAsync(
            () => admin.GetByRole(AriaRole.Button, new() { Name = "Download the report as signed off" }).ClickAsync(),
            new() { Timeout = 60_000 });

        Assert.EndsWith(" - as signed off.pdf", download.SuggestedFilename);
        var downloaded = await File.ReadAllBytesAsync(await download.PathAsync());
        Assert.Equal(bytes, downloaded);
        Assert.Empty(await CacheProblemsAsync(admin, [OfflineE2EWebAppFactory.TesterName, OfflineE2EWebAppFactory.RimuFarm]));
    }

    /// <summary>The report the device captured at sign-off for a test, base64 (null when none).</summary>
    private static Task<string?> CapturedOnDeviceAsync(IPage page, string testerId, string testId) =>
        page.EvaluateAsync<string?>(@"async ([testerId, testId]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(null);
                req.onsuccess = () => {
                    const db = req.result;
                    const get = db.transaction('reference').objectStore('reference').get('finalReport:' + testId);
                    get.onsuccess = () => {
                        db.close();
                        const bytes = get.result?.rows?.captured;
                        if (!bytes) { resolve(null); return; }
                        let binary = '';
                        for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
                        resolve(btoa(binary));
                    };
                    get.onerror = () => { db.close(); resolve(null); };
                };
            })", new[] { testerId, testId });

    // The device that has synced before holds the generator, so signing off with no signal captures
    // the report there and then — from the standards and letterhead of that moment — and that copy,
    // not one made after reconnecting, is what the server keeps.
    [Fact]
    public async Task A_report_signed_off_with_no_signal_is_captured_there_and_that_copy_is_what_lands()
    {
        var (context, page) = await TesterOnlineAndReadyAsync();
        await using var _ = context;
        await page.GotoAsync("/App/Tests");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        Assert.True(await PollAsync(page, @"async () => {
                const keys = [];
                for (const name of await caches.keys()) {
                    for (const r of await (await caches.open(name)).keys()) keys.push(new URL(r.url).pathname);
                }
                return ['/chunks/pdfmake-', '/chunks/vfs_fonts-'].every((c) => keys.some((k) => k.includes(c)));
            }", timeoutMs: 60_000), "the report generator was never warmed after the sync");
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

        await context.SetOfflineAsync(true);
        _factory.NetworkDown = true;
        var testId = await StartTestAsync(page);
        await SignOffAsync(page);

        string? captured = null;
        for (var i = 0; i < 150 && captured is null; i++)
        {
            captured = await CapturedOnDeviceAsync(page, _factory.TesterId, testId);
            if (captured is null) await Task.Delay(200);
        }
        Assert.True(captured is not null, "signing off offline didn't capture the report on the device");

        _factory.NetworkDown = false;
        await context.SetOfflineAsync(false);

        var stored = await StoredReportAsync(testId);
        Assert.True(stored is not null, "the captured report never reached the server after reconnecting");
        // No analyser PDF on this test, so the captured pages are the whole report.
        Assert.Equal(Convert.FromBase64String(captured!), StoredBytes(stored!.Value.Record));
    }

    // A device that has never synced doesn't hold the generator, and sign-off never downloads it
    // (with no signal that would break it for the rest of the page): the report is made when it's
    // sent instead, and still lands.
    [Fact]
    public async Task A_report_signed_off_with_no_signal_waits_on_the_device_and_lands_after_reconnecting()
    {
        var (context, page) = await TesterOnlineAndReadyAsync();
        await using var _ = context;
        // The farm book arrives in the background after the first tester page loads; offline, the
        // wizard needs it to start a test at the farm.
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

        await context.SetOfflineAsync(true);
        _factory.NetworkDown = true;
        var testId = await StartTestAsync(page);
        await SignOffAsync(page);

        Assert.True(await QueuedOnDeviceAsync(page, _factory.TesterId, testId, queued: true), "sign-off didn't queue the report");
        Assert.Null(await StoredReportAsync(testId, timeoutMs: 1_000));

        // Nothing pressed from here: the connection coming back is what sends the test, and the
        // report follows it.
        _factory.NetworkDown = false;
        await context.SetOfflineAsync(false);

        var stored = await StoredReportAsync(testId);
        Assert.True(stored is not null, "the queued report never reached the server after reconnecting");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(StoredBytes(stored!.Value.Record), 0, 5));
        Assert.True(await QueuedOnDeviceAsync(page, _factory.TesterId, testId, queued: false),
            "the report stayed queued on the device after it was sent");
        Assert.Empty(await CacheProblemsAsync(page, [OfflineE2EWebAppFactory.TesterName, OfflineE2EWebAppFactory.RimuFarm]));
    }
}
