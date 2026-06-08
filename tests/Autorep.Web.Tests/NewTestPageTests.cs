using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.App.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Tests;

// Page-model tests for the tester new-test flow (avoids the antiforgery/HTTP round-trip).
public class NewTestPageTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("newtest-" + Guid.NewGuid())
            .Options);

    private static NewModel TesterModel(AutorepDbContext db)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "tester-1") }, "Test"));
        return new NewModel(db)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    // Regression for PR #20: an inactive farm id (stale page / crafted POST) must be rejected.
    [Fact]
    public async Task OnPost_rejects_an_inactive_farm_and_creates_no_test()
    {
        using var db = NewDb();
        var farm = new Farm { Name = "Deactivated farm", IsActive = false };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = farm.Id;

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Errors.Should().NotBeEmpty();
        (await db.MachineTests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OnPost_creates_a_test_for_an_active_farm()
    {
        using var db = NewDb();
        var farm = new Farm { Name = "Active farm", IsActive = true };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = farm.Id;

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>()
            .Which.PageName.Should().Be("/App/Tests/Wizard");
        (await db.MachineTests.CountAsync()).Should().Be(1);
    }
}
