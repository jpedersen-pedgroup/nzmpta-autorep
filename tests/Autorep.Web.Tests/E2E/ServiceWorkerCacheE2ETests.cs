using System.Text.Json;
using Microsoft.Playwright;

namespace Autorep.Web.Tests.E2E;

// The app shell is only offline-capable if the service worker actually precaches it. Three
// things have silently failed here before and are cheap to pin down in a real browser:
// entries never landing at all, entries landing but never matching the versioned URL the page
// requests, and the cache name not tracking the build.
[Trait("Category", "E2E")]
public class ServiceWorkerCacheE2ETests : IClassFixture<E2EWebAppFactory>, IAsyncLifetime
{
    private readonly E2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public ServiceWorkerCacheE2ETests(E2EWebAppFactory factory) => _factory = factory;

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

    /// <summary>Everything the shell needs before a tester loses signal.</summary>
    private static readonly string[] Expected =
    [
        "/manifest.webmanifest",
        "/css/site.css",
        "/js/pwa-register.js",
        "/icons/icon.svg",
        "/img/logo-mpnz.svg",
        "/img/logo-mpnz-white.svg",
        "/img/logo-mpnz-lockup.svg",
        "/lib/fontawesome/css/all.min.css",
        "/lib/fontawesome/webfonts/fa-solid-900.woff2",
        "/lib/fonts/montserrat-latin.woff2",
        "/lib/fonts/montserrat-latin-ext.woff2",
        "/lib/fonts/opensans-latin.woff2",
        "/lib/fonts/opensans-latin-ext.woff2",
    ];

    [Fact]
    public async Task Service_worker_precaches_the_app_shell_and_serves_it_for_versioned_urls()
    {
        await using var context = await _browser.NewContextAsync(new()
        {
            BaseURL = _factory.BaseUrl,
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Account/Login");
        // Registration is deferred to window load, then install/activate is async.
        await page.EvaluateAsync("() => navigator.serviceWorker.ready");
        await page.WaitForFunctionAsync(
            "async () => (await caches.keys()).some((k) => k.startsWith('autorep-') " +
            "&& !k.includes('logos') && !k.includes('fontawesome'))",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var json = await page.EvaluateAsync<string>(@"async () => {
            const names = await caches.keys();
            const shell = names.find((k) => k.startsWith('autorep-')
                && !k.includes('logos') && !k.includes('fontawesome'));
            const cache = await caches.open(shell);
            const entries = (await cache.keys()).map((r) => new URL(r.url).pathname);
            // The page asks for assets with a cache-busting query today and may again; the
            // precached bare entry must still answer, or the precache is dead weight.
            const versioned = await caches.match(
                new Request(location.origin + '/css/site.css?v=probe'), { ignoreSearch: true });
            return JSON.stringify({ shell, entries, versionedHit: !!versioned });
        }");

        var result = JsonDocument.Parse(json).RootElement;
        var cacheName = result.GetProperty("shell").GetString()!;
        var entries = result.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet();

        Assert.All(Expected, asset =>
            Assert.True(entries.Contains(asset), $"{asset} was not precached (cache {cacheName})"));

        Assert.True(result.GetProperty("versionedHit").GetBoolean(),
            "a versioned request did not match the precached asset — the precache would be unused");

        // Stamped from the bundle by tools/stamp-sw.mjs. A hand-typed name would mean a deploy
        // never retires the previous build's assets, which with ignoreSearch pins devices to it.
        Assert.Matches("^autorep-[0-9a-f]{12}$", cacheName);
    }
}
