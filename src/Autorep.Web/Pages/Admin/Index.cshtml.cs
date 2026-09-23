using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;
    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    /// <summary>Field-created farms awaiting review, within the viewer's reach (the shared
    /// FarmScope predicate for a Company Administrator; everything for a Super-Administrator).</summary>
    public int PendingFarmCount { get; private set; }

    /// <summary>Farms whose next test date has passed, on the same terms as the Upcoming tests page
    /// (each farm's latest test, the viewer's company for a Company Administrator).</summary>
    public int OverdueTestCount { get; private set; }

    public async Task OnGetAsync()
    {
        IQueryable<Farm> q = _db.Farms.Where(f => f.PendingReviewSince != null);
        var isSuper = User.IsInRole(Roles.SuperAdministrator);
        var me = isSuper ? null : await _users.GetUserAsync(User);
        if (!isSuper) q = q.InCompanyScope(_db, me?.TestingCompanyId, me?.Id);
        PendingFarmCount = await q.CountAsync();

        // Due before today. A company admin without a company has no tests to be overdue.
        if (isSuper || me?.TestingCompanyId is not null)
        {
            var overdue = Tests.UpcomingModel.Query(_db, me?.TestingCompanyId, NzTime.Today.AddDays(-1));
            OverdueTestCount = await overdue.Select(r => r.FarmId).Distinct().CountAsync();
        }
    }
}
