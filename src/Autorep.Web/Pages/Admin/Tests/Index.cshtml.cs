using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public int PageSize { get; } = 50;
    public int TotalCount { get; private set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

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
            if (companyId is null) return; // an admin not attached to a company owns no tests

            var companyTesterIds = _db.Users
                .Where(u => u.TestingCompanyId == companyId)
                .Select(u => u.Id);
            query = query.Where(t => companyTesterIds.Contains(t.TesterId));
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(t =>
                (t.Farm != null && t.Farm.Name.Contains(term)) ||
                (t.Tester != null && t.Tester.Email != null && t.Tester.Email.Contains(term)));
        }

        TotalCount = await query.CountAsync();
        if (PageNumber < 1) PageNumber = 1;

        Tests = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
