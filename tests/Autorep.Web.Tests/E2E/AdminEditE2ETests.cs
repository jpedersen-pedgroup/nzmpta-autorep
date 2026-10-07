using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Services;
using Autorep.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Autorep.Web.Tests.E2E.OfflineBrowser;

namespace Autorep.Web.Tests.E2E;

// O2 and PRD stories 49–50 in a real browser: an administrator edits a synced, completed test from the
// admin portal; the edit is saved as the test's next version (never in place); the report regenerates
// from it with the amendment in its history; a Company Administrator's edit reaches the summary and
// recommendations only, which the SERVER enforces; and the tester's device picks the new version up on
// its next sync, locking its original.
[Trait("Category", "E2E")]
public class AdminEditE2ETests : IClassFixture<AdminEditE2EWebAppFactory>, IAsyncLifetime
{
    private readonly AdminEditE2EWebAppFactory _factory;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public AdminEditE2ETests(AdminEditE2EWebAppFactory factory) => _factory = factory;

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

    private async Task<(IBrowserContext Context, IPage Page)> SuperAdminAsync()
    {
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("#Input_Email", AdminEditE2EWebAppFactory.AdminEmail);
        await page.FillAsync("#Input_Password", OfflineE2EWebAppFactory.TesterPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => url.Contains("/Account/TwoFactorChallenge"));
        await page.FillAsync("#Input_Code", Totp.Now(_factory.AdminAuthenticatorKey));
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Account/"));
        return (context, page);
    }

    private async Task<(IBrowserContext Context, IPage Page)> CompanyAdminAsync()
    {
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await SignInAsync(page, AdminEditE2EWebAppFactory.CompanyAdminEmail, landsOn: "/Admin");
        return (context, page);
    }

    private static ILocator Step(IPage page, string title) => page.Locator(".wizard__step", new() { HasText = title });

    private static async Task<byte[]> DownloadAsync(IPage page, ILocator button)
    {
        var download = await page.RunAndWaitForDownloadAsync(() => button.ClickAsync(), new() { Timeout = 60_000 });
        var bytes = await File.ReadAllBytesAsync(await download.PathAsync());
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        return bytes;
    }

    /// <summary>Page objects in a pdfmake document (no attachment is appended, so no object streams).</summary>
    private static int PageCount(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;

    /// <summary>Saves a screenshot when E2E_SCREENSHOTS names a folder — for looking at the UI, never asserted.</summary>
    private static async Task ShotAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("E2E_SCREENSHOTS") is { Length: > 0 } dir)
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(dir, $"{name}.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled,
            });
    }

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.AppServices.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    [Fact]
    public async Task A_super_admin_edits_a_synced_test_into_a_new_version_and_the_report_regenerates()
    {
        var (testId, clientId) = await _factory.SeedCompletedTestAsync("Totara Terrace");
        var (context, page) = await SuperAdminAsync();
        await using var _ = context;

        await page.GotoAsync($"/Admin/Tests/View/{testId}");
        await Step(page, "Review & Sign-Off").ClickAsync();
        var original = await DownloadAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Download report (PDF)" }));

        await page.GetByRole(AriaRole.Button, new() { Name = "Edit as a new version" }).ClickAsync();
        await Step(page, "Fault Summary & Recommendations").ClickAsync();
        await page.FillAsync("#fault-notes", "Corrected by NZMPTA after the farmer's call");
        await page.FillAsync("#edit-reason", "Farmer rang with the right figures");
        await ShotAsync(page, "o2-super-admin-editing");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save as version 2" }).ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("saved=2", StringComparison.Ordinal));
        var saved = page.Locator(".admin-edit__saved");
        await saved.GetByText("Saved as version 2.").WaitForAsync();
        await ShotAsync(page, "o2-super-admin-saved");
        var amended = await DownloadAsync(page, saved.GetByRole(AriaRole.Button, new() { Name = "Download report (PDF)" }));

        Assert.True(PageCount(amended) > PageCount(original),
            $"the regenerated report should carry the amendment history page ({PageCount(original)} → {PageCount(amended)} pages)");

        // The new version's own report is kept on the server too — made in the browser right after the
        // save, as a tester's device keeps the one it signs off — and the viewer offers it "as saved".
        await saved.Locator("[data-saved-report='stored']").WaitForAsync(new() { Timeout = 60_000 });
        var version = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.SupersedesClientId == clientId));
        var kept = await WithDbAsync(db => db.FinalReportBlobs.SingleAsync(r => r.MachineTestId == version.Id));
        Assert.Equal(_factory.AdminId, kept.StoredBy);
        await Step(page, "Review & Sign-Off").ClickAsync();
        await page.Locator("[data-stored-report]", new() { HasText = "As saved" }).WaitForAsync();
        var asSaved = await DownloadAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Download the report as saved" }));
        Assert.Equal(kept.SizeBytes, asSaved.Length);

        Assert.Equal(2, version.Version);
        Assert.Equal(_factory.TesterId, version.TesterId);
        Assert.Equal(_factory.AdminId, version.AuthorId);
        Assert.Equal("Corrected by NZMPTA after the farmer's call", version.Notes);
        var record = JsonNode.Parse(version.PayloadJson!)!["amendments"]!.AsArray().Single()!;
        Assert.Equal("Farmer rang with the right figures", record["reason"]!.GetValue<string>());
        Assert.Contains(record["changes"]!.AsArray(), c => c!["label"]!.GetValue<string>() == "General comments");

        // Version 1 is still on record, unchanged, and now says it has been replaced.
        var v1 = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == testId));
        Assert.Contains("Original comment from the farm", v1.PayloadJson);
        await page.GotoAsync($"/Admin/Tests/View/{testId}");
        await page.GetByText("Version 1 has been replaced by version 2").WaitForAsync();
    }

    // O3's leftover: the analyser PDF that arrived after the visit, attached by NZMPTA as part of a new
    // version of the test.
    [Fact]
    public async Task A_super_admin_attaches_the_analyser_pdf_as_part_of_a_new_version()
    {
        var (testId, clientId) = await _factory.SeedCompletedTestAsync("Miro Meadows");
        var (context, page) = await SuperAdminAsync();
        await using var _ = context;

        await page.GotoAsync($"/Admin/Tests/View/{testId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit as a new version" }).ClickAsync();
        await Step(page, "Review & Sign-Off").ClickAsync();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% analyser export\n");
        await page.Locator(".dropzone input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "analyser-export.pdf", MimeType = "application/pdf", Buffer = pdf,
        });
        await page.Locator(".attach-chip", new() { HasText = "analyser-export.pdf" }).WaitForAsync();
        await page.FillAsync("#edit-reason", "The analyser export arrived after the visit");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save as version 2" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("saved=2", StringComparison.Ordinal));

        // The PDF went to the PDF store under the new version; its payload keeps the pointer.
        var version = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.SupersedesClientId == clientId));
        Assert.Null(PulsationPayload.Base64(version.PayloadJson));
        Assert.Equal(PdfHash.Sha256Hex(pdf), PulsationPayload.StoredSha256(version.PayloadJson));
        var changes = JsonNode.Parse(version.PayloadJson!)!["amendments"]!.AsArray().Single()!["changes"]!.AsArray();
        Assert.Contains(changes, c => c!["label"]!.GetValue<string>() == "Pulsation analyser attachment");
        await Step(page, "Review & Sign-Off").ClickAsync();
        await page.Locator(".attach-chip", new() { HasText = "analyser-export.pdf" }).WaitForAsync();
        // And the viewer's route serves it back from the store (fetched as the page does, signed in).
        var served = await page.EvaluateAsync<int[]>(
            "async (url) => Array.from(new Uint8Array(await (await fetch(url)).arrayBuffer()))",
            $"/api/tests/{version.Id}/pulsation-pdf");
        Assert.Equal(pdf, served.Select(b => (byte)b).ToArray());
    }

    [Fact]
    public async Task A_company_admin_changes_a_recommendation_but_the_server_refuses_a_reading_change()
    {
        var (testId, clientId) = await _factory.SeedCompletedTestAsync("Rata Rise");
        var (context, page) = await CompanyAdminAsync();
        await using var _ = context;

        await page.GotoAsync($"/Admin/Tests/View/{testId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit summary & recommendations" }).ClickAsync();
        // The edit opens on the Fault Summary, and every other step stays read-only.
        var recommendation = page.Locator(".fault__rec").First;
        await recommendation.FillAsync("Clean and re-oil the wicks before the next milking");
        await page.FillAsync("#edit-reason", "Clearer wording for the farmer");
        await Step(page, "Vacuum Tests").ClickAsync();
        Assert.True(await page.Locator(".wizard__panel input[type=number], .wizard__panel input[inputmode=decimal]").First.IsDisabledAsync(),
            "a reading can't be edited in a Company Administrator's edit");
        await ShotAsync(page, "o2-company-admin-editing");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save as version 2" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("saved=2", StringComparison.Ordinal));

        var version = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.SupersedesClientId == clientId));
        Assert.Equal(_factory.CompanyAdminId, version.AuthorId);
        Assert.Equal("Clean and re-oil the wicks before the next milking",
            JsonNode.Parse(version.PayloadJson!)!["recommendations"]!["vp.wick"]!.GetValue<string>());

        // The UI won't let a reading change through; the server refuses one sent anyway.
        var answer = JsonDocument.Parse(await page.EvaluateAsync<string>(@"async (id) => {
                const view = await (await fetch(`/api/tests/${id}`)).json();
                const p = JSON.parse(view.payloadJson);
                p.readings['tr.workingVacuum'] = 40;
                p.amendments = [...(p.amendments ?? []), { version: 3, amendedAt: new Date().toISOString(), baseVersion: 2, changes: [] }];
                const res = await fetch(`/api/admin/tests/${id}/versions`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ payloadJson: JSON.stringify(p), reason: 'Trying a reading' }),
                });
                return JSON.stringify({ status: res.status, body: await res.json() });
            }", version.Id.ToString())).RootElement;
        Assert.Equal(422, answer.GetProperty("status").GetInt32());
        Assert.Contains("readings.tr.workingVacuum",
            answer.GetProperty("body").GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.False(await WithDbAsync(db => db.MachineTests.AnyAsync(t => t.SupersedesClientId == version.ClientId)),
            "nothing is saved when a field is out of the editor's scope");
    }

    [Fact]
    public async Task A_testers_device_pulls_the_admin_version_and_its_original_locks()
    {
        var (testId, clientId) = await _factory.SeedCompletedTestAsync("Kahu Corner");
        var (context, page) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = context;
        await SignInAsync(page);
        await page.GotoAsync("/App/Tests");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        var rows = page.Locator("tr", new() { HasText = "Kahu Corner" });
        await rows.GetByRole(AriaRole.Button, new() { Name = "Edit" }).WaitForAsync();

        // An administrator edits the test online meanwhile.
        var adminVersion = await SaveAdminVersionAsync(testId, p => p["notes"] = "Comment corrected by NZMPTA");

        await page.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        var original = rows.Filter(new() { HasText = "superseded" });
        var current = rows.Filter(new() { HasText = "edited by admin" });
        await current.WaitForAsync();
        Assert.Equal(1, await original.CountAsync());
        Assert.Equal(0, await original.GetByRole(AriaRole.Button, new() { Name = "Edit" }).CountAsync());
        Assert.Equal(1, await current.GetByRole(AriaRole.Button, new() { Name = "Edit" }).CountAsync());
        await ShotAsync(page, "o2-tester-list-after-admin-edit");

        var stored = JsonDocument.Parse((await LocalTestJsonAsync(page, _factory.TesterId, adminVersion.ToString()))!).RootElement;
        Assert.Equal(clientId.ToString(), stored.GetProperty("supersedesId").GetString());
        Assert.Equal("Comment corrected by NZMPTA", stored.GetProperty("notes").GetString());

        // The original opens read-only; the new version says who made it and why.
        await page.GotoAsync($"/App/Tests/Wizard?id={clientId}");
        await page.GetByText("Read-only").First.WaitForAsync();
        await page.GotoAsync($"/App/Tests/Wizard?id={adminVersion}");
        await page.Locator("[data-version-note=admin]", new() { HasText = $"made by {AdminEditE2EWebAppFactory.AdminName} (Super Administrator)" }).WaitForAsync();
    }

    // PRD story 70: NZMPTA deletes a test (with a reason) and it leaves the tester's device at the next
    // sync — said out loud, not silently.
    [Fact]
    public async Task A_soft_deleted_test_leaves_the_testers_device()
    {
        var (testId, clientId) = await _factory.SeedCompletedTestAsync("Kereru Knoll");
        var (testerContext, tester) = await NewTesterPageAsync(_browser, _factory.BaseUrl);
        await using var _ = testerContext;
        await SignInAsync(tester);
        await tester.GotoAsync("/App/Tests");
        await tester.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        var row = tester.Locator("tr", new() { HasText = "Kereru Knoll" });
        await row.WaitForAsync();

        var (adminContext, admin) = await SuperAdminAsync();
        await using var __ = adminContext;
        await admin.GotoAsync($"/Admin/Tests/View/{testId}");
        await admin.GetByRole(AriaRole.Button, new() { Name = "Delete test…" }).ClickAsync();
        await admin.FillAsync("#delete-reason", "Duplicate of another test");
        await ShotAsync(admin, "o2-delete-dialog");
        await admin.GetByRole(AriaRole.Button, new() { Name = "Delete test", Exact = true }).ClickAsync();
        await admin.Locator(".admin-edit__deleted", new() { HasText = "Duplicate of another test" }).WaitForAsync();
        await ShotAsync(admin, "o2-admin-deleted-view");

        await tester.GetByRole(AriaRole.Button, new() { Name = "Sync now" }).ClickAsync();
        await tester.Locator("[data-removed-tests]", new() { HasText = "Kereru Knoll" }).WaitForAsync();
        Assert.Equal(0, await row.CountAsync());
        Assert.Null(await LocalTestJsonAsync(tester, _factory.TesterId, clientId.ToString()));
        await ShotAsync(tester, "o2-tester-after-delete");

        var deleted = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == testId));
        Assert.True(deleted.IsDeleted);
        Assert.Equal("Duplicate of another test", deleted.DeletedReason);
    }

    /// <summary>An administrator's version of the test, made through the same service the portal uses.</summary>
    private async Task<Guid> SaveAdminVersionAsync(Guid testId, Action<JsonObject> change)
    {
        using var scope = _factory.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var versioning = scope.ServiceProvider.GetRequiredService<AdminVersioning>();
        var payload = JsonNode.Parse((await db.MachineTests.SingleAsync(t => t.Id == testId)).PayloadJson!)!.AsObject();
        change(payload);
        payload["amendments"] = new JsonArray
        {
            new JsonObject
            {
                ["version"] = 2, ["amendedAt"] = DateTimeOffset.UtcNow.ToString("o"), ["baseVersion"] = 1,
                ["changes"] = new JsonArray { new JsonObject { ["section"] = "Other", ["label"] = "General comments", ["from"] = "Original", ["to"] = "Corrected" } },
            },
        };
        var result = await versioning.SaveAsync(db.MachineTests, testId, payload.ToJsonString(), "Corrected on the farmer's call",
            new AdminVersioning.Editor(_factory.AdminId, AdminEditE2EWebAppFactory.AdminEmail, AdminEditE2EWebAppFactory.AdminName, true),
            CancellationToken.None);
        var saved = Assert.IsType<AdminVersioning.Saved>(result);
        return saved.Version.ClientId!.Value;
    }
}
