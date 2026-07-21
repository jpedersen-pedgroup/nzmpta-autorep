using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.Admin.Farms;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Tests;

// Page-model tests for the admin farm Edit page's review-approval flow.
public class FarmEditPageTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("farmedit-" + Guid.NewGuid())
            .Options);

    private static EditModel ModelAs(AutorepDbContext db, string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        // Store-only UserManager: the page only calls GetUserAsync, which needs just the store.
        var users = new UserManager<Tester>(
            new UserStore<Tester>(db), null!, null!, null!, null!, null!, null!, null!, null!);
        return new EditModel(db, users)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    [Fact]
    public async Task Approve_clears_the_pending_flag()
    {
        using var db = NewDb();
        var farm = new Farm
        {
            Name = "Pending farm",
            PendingReviewSince = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByTesterId = null,
        };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = ModelAs(db, "super-1", Roles.SuperAdministrator);
        model.Id = farm.Id;

        var result = await model.OnPostApproveAsync();

        result.Should().BeOfType<PageResult>();
        (await db.Farms.SingleAsync(f => f.Id == farm.Id)).PendingReviewSince.Should().BeNull();
        model.Message.Should().NotBeNull();
    }

    // The approve handler must honour the same scope guard as editing: a Company Administrator
    // from another company can't approve (or even see) a farm outside their scope.
    [Fact]
    public async Task Approve_is_forbidden_outside_the_admins_company_scope()
    {
        using var db = NewDb();
        var owningCompany = new TestingCompany { Name = "Owning Co" };
        var otherCompany = new TestingCompany { Name = "Other Co" };
        db.TestingCompanies.AddRange(owningCompany, otherCompany);
        db.Users.Add(new Tester { Id = "other-admin", UserName = "other-admin", TestingCompanyId = otherCompany.Id });
        var farm = new Farm
        {
            Name = "Foreign pending farm",
            CreatedByTestingCompanyId = owningCompany.Id,
            PendingReviewSince = DateTimeOffset.UtcNow,
        };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = ModelAs(db, "other-admin", Roles.CompanyAdministrator);
        model.Id = farm.Id;

        var result = await model.OnPostApproveAsync();

        result.Should().BeOfType<ForbidResult>();
        (await db.Farms.SingleAsync(f => f.Id == farm.Id)).PendingReviewSince.Should().NotBeNull();
    }
}
