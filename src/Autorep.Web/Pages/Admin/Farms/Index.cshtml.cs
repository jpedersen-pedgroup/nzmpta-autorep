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
        q = q.OrderBy(f => f.Name);

        // The Tests count matches the viewer's reach: a Company Administrator sees their own
        // company's tests on the farm (multi-company farms carry other companies' history too),
        // a Super-Administrator sees the total.
        Farms = ScopedView
            ? await q.Select(f => new Row(
                f.Id, f.Name, f.Region!.Name, f.MilkSupplyCompany!.Name, f.Town, f.IsActive,
                _db.MachineTests.Count(t => t.FarmId == f.Id
                    && _db.Users.Any(u => u.Id == t.TesterId && u.TestingCompanyId == companyId))))
                .ToListAsync()
            : await q.Select(f => new Row(
                f.Id, f.Name, f.Region!.Name, f.MilkSupplyCompany!.Name, f.Town, f.IsActive,
                _db.MachineTests.Count(t => t.FarmId == f.Id)))
                .ToListAsync();
    }
}
