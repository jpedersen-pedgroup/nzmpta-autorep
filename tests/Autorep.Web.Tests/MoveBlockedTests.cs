using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Josh, 27 Oct: unsynced tests block a move. A tester can't be moved to another Testing Company while
// one of their devices last reported tests it hasn't sent (DeviceOutbox, from
// Client/sync/outboxReport.ts), or while they have a test on the server that isn't signed off. Each
// has to be sent (or deleted on the device) and finished (or deleted) first.
public class MoveBlockedTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public MoveBlockedTests(AuthedWebAppFactory factory) => _factory = factory;

    // ---- The device's report ------------------------------------------------------------------

    [Fact]
    public async Task A_device_report_is_kept_per_device_and_replaced_by_the_next()
    {
        var (testerId, _, _) = await SeedTesterAsync();
        var tester = _factory.CreateClientAs(Roles.Tester, testerId);
        var ipad = Guid.NewGuid();
        var phone = Guid.NewGuid();

        (await Report(tester, ipad, testerId, 3, "iPad · Safari")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Report(tester, phone, testerId, 1)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Report(tester, ipad, testerId, 0, "iPad · Safari")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await WithDbAsync(db => db.DeviceOutboxes.Where(d => d.TesterId == testerId).OrderBy(d => d.UnsentTests).ToListAsync());
        rows.Select(r => (r.DeviceId, r.UnsentTests)).Should().Equal((ipad, 0), (phone, 1));
        rows[0].Device.Should().Be("iPad · Safari");
        var keys = rows.Select(r => r.Id.ToString()).ToList();
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityType == nameof(DeviceOutbox) && keys.Contains(a.EntityKey))))
            .Should().Be(0, "a device's status report isn't a record worth auditing");
    }

    [Fact]
    public async Task A_report_about_someone_elses_store_is_refused()
    {
        var (testerId, _, _) = await SeedTesterAsync();
        var tester = _factory.CreateClientAs(Roles.Tester, testerId);

        (await Report(tester, Guid.NewGuid(), "previous-tester-on-this-device", 4)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Report(tester, Guid.Empty, testerId, 1)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Report(tester, Guid.NewGuid(), testerId, -1)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await WithDbAsync(db => db.DeviceOutboxes.AnyAsync(d => d.TesterId == testerId))).Should().BeFalse();
    }

    // ---- What blocks a move ---------------------------------------------------------------------

    [Fact]
    public async Task Unsent_tests_on_a_device_block_the_move_until_it_reports_none()
    {
        var (testerId, from, to) = await SeedTesterAsync();
        var device = Guid.NewGuid();
        await Report(_factory.CreateClientAs(Roles.Tester, testerId), device, testerId, 2, "iPad · Safari");

        var page = Decode(await SuperAdmin().GetStringAsync($"/Admin/Testers/Edit/{testerId}"));
        page.Should().Contain("can't be moved yet").And.Contain("2 unsent tests on 1 device")
            .And.Contain("iPad · Safari (…").And.Contain("Forget this device");

        var refused = Decode(await (await ChangeCompanyAsync(testerId, to)).Content.ReadAsStringAsync());
        refused.Should().Contain("They can't be moved yet: 2 unsent tests on 1 device.");
        (await CompanyOfAsync(testerId)).Should().Be(from);

        await Report(_factory.CreateClientAs(Roles.Tester, testerId), device, testerId, 0, "iPad · Safari");
        await ChangeCompanyAsync(testerId, to);
        (await CompanyOfAsync(testerId)).Should().Be(to, "the device has sent everything");
    }

    [Fact]
    public async Task An_unfinished_test_on_the_server_blocks_the_move_until_it_is_signed_off_or_deleted()
    {
        var (testerId, from, to) = await SeedTesterAsync();
        var (draftId, farmName) = await WithDbAsync(async db =>
        {
            var farm = new Farm { Name = "Unfinished Farm " + Guid.NewGuid().ToString("N")[..6], CreatedByTestingCompanyId = from };
            db.Farms.Add(farm);
            var draft = new MachineTest { TesterId = testerId, FarmId = farm.Id, TestingCompanyId = from, ClientId = Guid.NewGuid() };
            db.MachineTests.AddRange(draft,
                new MachineTest { TesterId = testerId, FarmId = farm.Id, TestingCompanyId = from, ClientId = Guid.NewGuid(), MarkedCompleteAt = DateTimeOffset.UtcNow },
                new MachineTest { TesterId = testerId, FarmId = farm.Id, TestingCompanyId = from, ClientId = Guid.NewGuid(), IsDeleted = true });
            await db.SaveChangesAsync();
            return (draft.Id, farm.Name);
        });

        var page = Decode(await SuperAdmin().GetStringAsync($"/Admin/Testers/Edit/{testerId}"));
        page.Should().Contain("1 unfinished test on the server").And.Contain($"/Admin/Tests/View/{draftId}").And.Contain(farmName);
        await ChangeCompanyAsync(testerId, to);
        (await CompanyOfAsync(testerId)).Should().Be(from);

        await WithDbAsync(async db =>
        {
            (await db.MachineTests.SingleAsync(t => t.Id == draftId)).MarkedCompleteAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });
        await ChangeCompanyAsync(testerId, to);
        (await CompanyOfAsync(testerId)).Should().Be(to, "signed-off and deleted tests don't hold a move up");
    }

    [Fact]
    public async Task Forgetting_a_lost_device_lifts_its_block_and_is_audited()
    {
        var (testerId, from, to) = await SeedTesterAsync();
        await Report(_factory.CreateClientAs(Roles.Tester, testerId), Guid.NewGuid(), testerId, 5, "iPad · Safari");
        var rowId = await WithDbAsync(db => db.DeviceOutboxes.Where(d => d.TesterId == testerId).Select(d => d.Id).SingleAsync());

        var res = await PostEditAsync(testerId, "ForgetDevice", [new("ForgetDeviceId", rowId.ToString())]);
        Decode(await res.Content.ReadAsStringAsync()).Should().Contain("Forgot iPad · Safari (…");

        (await WithDbAsync(db => db.DeviceOutboxes.AnyAsync(d => d.TesterId == testerId))).Should().BeFalse();
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityType == nameof(DeviceOutbox) && a.Operation == "Deleted")))
            .Should().BeTrue();
        await ChangeCompanyAsync(testerId, to);
        (await CompanyOfAsync(testerId)).Should().Be(to);
    }

    [Fact]
    public async Task A_clear_tester_shows_when_their_devices_last_reported()
    {
        var (testerId, _, _) = await SeedTesterAsync();
        var html = Decode(await SuperAdmin().GetStringAsync($"/Admin/Testers/Edit/{testerId}"));
        html.Should().Contain("None of their devices has reported in yet").And.NotContain("can't be moved yet");

        await Report(_factory.CreateClientAs(Roles.Tester, testerId), Guid.NewGuid(), testerId, 0);
        Decode(await SuperAdmin().GetStringAsync($"/Admin/Testers/Edit/{testerId}"))
            .Should().Contain("Their devices last reported nothing waiting to be sent");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Decode(string html) => WebUtility.HtmlDecode(html);

    private static Task<HttpResponseMessage> Report(HttpClient client, Guid deviceId, string testerId, int unsent, string? device = null) =>
        client.PostAsJsonAsync("/api/sync/outbox", new { deviceId, testerId, unsentTests = unsent, device });

    private HttpClient SuperAdmin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, Roles.SuperAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "move-blocked-super");
        return client;
    }

    private Task<HttpResponseMessage> ChangeCompanyAsync(string testerId, Guid to) =>
        PostEditAsync(testerId, "ChangeCompany", [new("NewCompanyId", to.ToString())]);

    private async Task<HttpResponseMessage> PostEditAsync(string testerId, string handler, List<KeyValuePair<string, string>> fields)
    {
        var client = SuperAdmin();
        var page = await client.GetAsync($"/Admin/Testers/Edit/{testerId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = page.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];
        fields.Add(new("__RequestVerificationToken", token));
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Testers/Edit/{testerId}?handler={handler}")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        req.Headers.Add("Cookie", cookie);
        return await client.SendAsync(req);
    }

    /// <summary>A Tester at one company, and another company to move them to.</summary>
    private async Task<(string TesterId, Guid From, Guid To)> SeedTesterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        var from = new TestingCompany { Name = "From Co " + Guid.NewGuid().ToString("N")[..6] };
        var to = new TestingCompany { Name = "To Co " + Guid.NewGuid().ToString("N")[..6] };
        db.TestingCompanies.AddRange(from, to);
        await db.SaveChangesAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();
        var email = $"mover-{Guid.NewGuid():N}@test.local";
        var tester = new Tester { UserName = email, Email = email, DisplayName = "Mo Mover", TestingCompanyId = from.Id };
        (await users.CreateAsync(tester)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync(tester, Roles.Tester)).Succeeded.Should().BeTrue();
        return (tester.Id, from.Id, to.Id);
    }

    private Task<Guid?> CompanyOfAsync(string userId) =>
        WithDbAsync(db => db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TestingCompanyId).SingleAsync());

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }
}
