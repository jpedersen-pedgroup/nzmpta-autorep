using Microsoft.Playwright;

namespace Autorep.Web.Tests.E2E;

/// <summary>Browser steps the offline suite repeats. Every wait is bounded: an unbounded one
/// doesn't fail a CI run, it hangs the job until the workflow times out.</summary>
internal static class OfflineBrowser
{
    public const int WaitMs = 30_000;

    /// <summary>
    /// Re-evaluates an ASYNC page check until it returns true or the time runs out. Not
    /// WaitForFunctionAsync: an async function returns a Promise, which is always truthy, so that
    /// would resolve at once without waiting (see ServiceWorkerCacheE2ETests). Each evaluation is
    /// bounded too, so a promise that never settles fails rather than hangs. Survives the page
    /// navigating underneath it (an evaluation torn down by a reload just counts as "not yet").
    /// </summary>
    public static async Task<bool> PollAsync(IPage page, string asyncCheck, object? arg = null, int timeoutMs = WaitMs)
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
            try
            {
                if (await check) return true;
            }
            catch (PlaywrightException)
            {
                // Execution context destroyed by a navigation — try again on the new document.
            }
            await Task.Delay(150);
        }
        return false;
    }

    public static async Task<(IBrowserContext Context, IPage Page)> NewTesterPageAsync(IBrowser browser, string baseUrl)
    {
        var context = await browser.NewContextAsync(new() { BaseURL = baseUrl, AcceptDownloads = true });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(WaitMs);
        page.SetDefaultNavigationTimeout(WaitMs);
        return (context, page);
    }

    public static async Task SignInAsync(IPage page, string email = OfflineE2EWebAppFactory.TesterEmail, string landsOn = "/App")
    {
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("#Input_Email", email);
        await page.FillAsync("#Input_Password", OfflineE2EWebAppFactory.TesterPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => url.Contains(landsOn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a tester's cached farm book on this device names <paramref name="farm"/>.</summary>
    public static Task<bool> FarmBookHasAsync(IPage page, string testerId, string farm) =>
        page.EvaluateAsync<bool>(@"async ([testerId, farm]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(false);
                req.onupgradeneeded = () => { req.transaction.abort(); };
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('reference')) { db.close(); resolve(false); return; }
                    const get = db.transaction('reference').objectStore('reference').get('farms');
                    get.onsuccess = () => { db.close(); resolve((get.result?.rows ?? []).some((f) => f.name === farm)); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })", new[] { testerId, farm });

    /// <summary>
    /// Online, the device must hold three things before signal goes: a worker controlling the page,
    /// a precache with the shell and the bundle in it, and the identity record the shell reads.
    /// </summary>
    public static async Task WaitUntilReadyForOfflineAsync(IPage page)
    {
        Assert.True(await PollAsync(page, @"async () => {
                const reg = await navigator.serviceWorker.getRegistration();
                return !!(reg && reg.active && navigator.serviceWorker.controller);
            }"), "the service worker never took control of the page");

        Assert.True(await PollAsync(page, @"async () => {
                const name = (await caches.keys()).find((k) => /^autorep-[0-9a-f]{12}$/.test(k));
                if (!name) return false;
                const have = new Set((await (await caches.open(name)).keys()).map((r) => new URL(r.url).pathname));
                return have.has('/app-shell.html') && have.has('/js/dist/autorep.js');
            }"), "the shell and the bundle were never precached");

        Assert.True(await PollAsync(page, @"async () => new Promise((resolve) => {
                const req = indexedDB.open('autorep-identity');
                req.onerror = () => resolve(false);
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('identity')) { db.close(); resolve(false); return; }
                    const get = db.transaction('identity').objectStore('identity').get('current');
                    get.onsuccess = () => { db.close(); resolve(!!(get.result && get.result.testerId)); };
                    get.onerror = () => { db.close(); resolve(false); };
                };
            })"), "the identity record was never written");
    }

    /// <summary>The shell is what's showing (not a server-rendered page).</summary>
    public static Task<bool> IsShellAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => document.body.hasAttribute('data-app-shell')");

    /// <summary>A test as stored on the device, as JSON (null when absent).</summary>
    public static Task<string?> LocalTestJsonAsync(IPage page, string testerId, string testId) =>
        page.EvaluateAsync<string?>(@"async ([testerId, testId]) => new Promise((resolve) => {
                const req = indexedDB.open('autorep_' + testerId);
                req.onerror = () => resolve(null);
                req.onsuccess = () => {
                    const db = req.result;
                    if (!db.objectStoreNames.contains('tests')) { db.close(); resolve(null); return; }
                    const get = db.transaction('tests').objectStore('tests').get(testId);
                    get.onsuccess = () => { db.close(); resolve(get.result ? JSON.stringify(get.result) : null); };
                    get.onerror = () => { db.close(); resolve(null); };
                };
            })", new[] { testerId, testId });
}
