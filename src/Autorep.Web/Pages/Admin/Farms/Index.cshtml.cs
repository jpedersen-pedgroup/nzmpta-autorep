using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Farms;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;
    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    public record Row(Guid Id, string Name, string? Region, string? MilkCompany, string? Town, bool IsActive, int TestCount, DateTimeOffset? PendingReviewSince);
    public IList<Row> Farms { get; private set; } = [];
    public bool ScopedView { get; private set; }
    public int PendingReviewCount => Farms.Count(f => f.PendingReviewSince is not null);

    /// <summary>"Dismiss" on the farms-awaiting-review reminder (Pages/Shared/_AdminLayout.cshtml),
    /// which every admin page shows: hides it for these farms, for this administrator only, then
    /// goes back to the page they were on.</summary>
    public async Task<IActionResult> OnPostDismissReviewAsync(List<Guid> farmIds, string? returnUrl)
    {
        await FarmReviewReminders.DismissAsync(_db, User, farmIds, DateTimeOffset.UtcNow);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Page("/Admin/Farms/Index")!);
    }

    public async Task OnGetAsync()
    {
        IQueryable<Farm> q = _db.Farms;

        // Super-Administrator sees all farms. A Company Administrator sees only farms in their
        // company's scope — set up or tested by the company (the shared FarmScope predicate,
        // matching the tester farm picker and the farm snapshot API).
        Guid? companyId = null;
        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            ScopedView = true;
            var me = await _users.GetUserAsync(User);
            companyId = me?.TestingCompanyId;
            q = q.InCompanyScope(_db, companyId, me?.Id);
        }
        // Farms awaiting review float to the top — this list doubles as the review queue.
        q = q.OrderBy(f => f.PendingReviewSince == null).ThenBy(f => f.Name);

        // The Tests count matches the viewer's reach: a Company Administrator sees the tests done
        // for their company on the farm (by the company stamped on each test, as the Tests list
        // scopes them; multi-company farms carry other companies' history too), a
        // Super-Administrator sees the total.
        Farms = ScopedView
            ? await q.Select(f => new Row(
                f.Id, f.Name, f.Region!.Name, f.MilkSupplyCompany!.Name, f.Town, f.IsActive,
                _db.MachineTests.Count(t => t.FarmId == f.Id && companyId != null && t.TestingCompanyId == companyId),
                f.PendingReviewSince))
                .ToListAsync()
            : await q.Select(f => new Row(
                f.Id, f.Name, f.Region!.Name, f.MilkSupplyCompany!.Name, f.Town, f.IsActive,
                _db.MachineTests.Count(t => t.FarmId == f.Id),
                f.PendingReviewSince))
                .ToListAsync();
    }
}
