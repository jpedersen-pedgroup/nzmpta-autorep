using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Tests;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;

    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    public IList<MachineTest> Tests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        IQueryable<MachineTest> query = _db.MachineTests
            .Include(t => t.Tester)
            .Include(t => t.Farm);

        // A Company Administrator may only see tests performed by Testers in
        // their own testing company; a Super-Administrator sees everything.
        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            var companyId = (await _users.GetUserAsync(User))?.TestingCompanyId;
            if (companyId is null)
            {
                // An admin not attached to a company owns no tests.
                Tests = [];
                return;
            }

            var companyTesterIds = _db.Users
                .Where(u => u.TestingCompanyId == companyId)
                .Select(u => u.Id);
            query = query.Where(t => companyTesterIds.Contains(t.TesterId));
        }

        Tests = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
