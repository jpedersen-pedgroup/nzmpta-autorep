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

    /// <summary>
    /// Re-evaluates an ASYNC page check until it returns true or <paramref name="timeoutMs"/> runs
    /// out. Not WaitForFunctionAsync: that polls for a truthy *return value*, and an async
    /// function returns a Promise — always truthy — so it resolves at once without waiting at all.
    /// Both waits in this test used it that way, which made them no-ops: the test only passed when
    /// the worker happened to finish precaching before the final read, and a slow CI runner caught
    /// it with no cache opened yet. Each evaluation is bounded too, so a promise that never
    /// settles fails the test instead of hanging the CI job.
    /// </summary>
    private static async Task<bool> PollAsync(IPage page, string asyncCheck, object? arg, int timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var check = page.EvaluateAsync<bool>(asyncCheck, arg);
            if (await Task.WhenAny(check, Task.Delay(remaining)) != check)
            {
                _ = check.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return false;
            }
            if (await check) return true;
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task Service_worker_precaches_the_app_shell_and_serves_it_for_versioned_urls()
    {
        // Plain HTTP on purpose: Chromium blocks service workers on an origin with a certificate
        // error, and the dev cert is untrusted on CI. localhost over HTTP is a secure context.
        await using var context = await _browser.NewContextAsync(new()
        {
            BaseURL = _factory.BaseUrlHttp,
        });
        var page = await context.NewPageAsync();
        // Every wait below is bounded. An unbounded one here does not fail the test, it hangs the
        // whole CI job until the workflow times out.
        page.SetDefaultTimeout(30_000);

        await page.GotoAsync("/Account/Login");

        // Poll for an activated worker rather than awaiting navigator.serviceWorker.ready, which
        // never settles when registration was refused and would therefore hang rather than fail.
        var active = await PollAsync(page,
            @"async () => {
                if (!('serviceWorker' in navigator)) return false;
                const reg = await navigator.serviceWorker.getRegistration();
                return !!(reg && reg.active);
            }",
            null);
        Assert.True(active, "the service worker never activated within 30 s");

        // Wait for the shell to be fully POPULATED, not merely for a cache to exist — precaching
        // is asynchronous, so "a cache named autorep-* is present" is true a moment before its
        // entries are. A miss isn't fatal here: the assertions below say exactly what was
        // missing, which a bare timeout would not.
        await PollAsync(page,
            @"async (expected) => {
                const names = await caches.keys();
                const shell = names.find((k) => k.startsWith('autorep-')
                    && !k.includes('logos') && !k.includes('fontawesome') && !k.includes('guides'));
                if (!shell) return false;
                const cache = await caches.open(shell);
                const have = new Set((await cache.keys()).map((r) => new URL(r.url).pathname));
                return expected.every((e) => have.has(e));
            }",
            Expected);

        // Every key is always present (null rather than undefined, which JSON.stringify drops)
        // so a failure reports what the browser actually had instead of a missing-key error.
        var json = await page.EvaluateAsync<string>(@"async () => {
            const names = await caches.keys();
            const shell = names.find((k) => k.startsWith('autorep-')
                && !k.includes('logos') && !k.includes('fontawesome') && !k.includes('guides')) ?? null;
            const entries = shell
                ? (await (await caches.open(shell)).keys()).map((r) => new URL(r.url).pathname)
                : [];
            // The page asks for assets with a cache-busting query today and may again; the
            // precached bare entry must still answer, or the precache is dead weight.
            const versioned = await caches.match(
                new Request(location.origin + '/css/site.css?v=probe'), { ignoreSearch: true });
            return JSON.stringify({ names, shell, entries, versionedHit: !!versioned });
        }");

        var result = JsonDocument.Parse(json).RootElement;
        Assert.True(result.GetProperty("shell").ValueKind == JsonValueKind.String,
            $"no app-shell cache was found. Browser reported: {json}");

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
