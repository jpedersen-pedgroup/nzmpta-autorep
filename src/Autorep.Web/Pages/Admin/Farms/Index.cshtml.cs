using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
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

    public record Row(Guid Id, string Name, string? Region, string? MilkCompany, string? Town, bool IsActive, int TestCount);
    public IList<Row> Farms { get; private set; } = [];
    public bool ScopedView { get; private set; }

    public async Task OnGetAsync()
    {
        IQueryable<Farm> q = _db.Farms;

        // Super-Administrator sees all farms. A Company Administrator sees only farms
        // that have a completed Machine Test by a Tester in their Testing Company.
        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            ScopedView = true;
            var me = await _users.GetUserAsync(User);
            var companyId = me?.TestingCompanyId;
            q = q.Where(f => _db.MachineTests.Any(t =>
                t.FarmId == f.Id
                && t.MarkedCompleteAt != null
                && _db.Users.Any(u => u.Id == t.TesterId && u.TestingCompanyId == companyId)));
        }

        Farms = await q
            .OrderBy(f => f.Name)
            .Select(f => new Row(
                f.Id,
                f.Name,
                f.Region!.Name,
                f.MilkSupplyCompany!.Name,
                f.Town,
                f.IsActive,
                _db.MachineTests.Count(t => t.FarmId == f.Id)))
            .ToListAsync();
    }
}
