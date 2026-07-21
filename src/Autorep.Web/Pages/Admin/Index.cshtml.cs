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

    public async Task OnGetAsync()
    {
        IQueryable<Farm> q = _db.Farms.Where(f => f.PendingReviewSince != null);
        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            var me = await _users.GetUserAsync(User);
            q = q.InCompanyScope(_db, me?.TestingCompanyId, me?.Id);
        }
        PendingFarmCount = await q.CountAsync();
    }
}
