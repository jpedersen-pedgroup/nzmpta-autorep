using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    [BindProperty(SupportsGet = true)] public Guid? FarmId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CompanyId { get; set; }
    [BindProperty(SupportsGet = true)] public string? TesterId { get; set; }
    public string? FarmName { get; private set; }
    public List<SelectListItem> CompanyOptions { get; private set; } = [];
    public List<SelectListItem> TesterOptions { get; private set; } = [];
    public bool HasFilter => !string.IsNullOrWhiteSpace(Q) || CompanyId is not null || !string.IsNullOrEmpty(TesterId);
    public int PageSize { get; } = 50;
    public int TotalCount { get; private set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public async Task OnGetAsync()
    {
        IQueryable<MachineTest> query = _db.MachineTests
            .Include(t => t.Tester)
            .Include(t => t.Farm);

        // A Company Administrator may only see tests performed by Testers in
        // their own testing company (the company filter is pinned for them);
        // a Super-Administrator sees everything and may filter by any company.
        Guid? scopeCompanyId;
        string? meId = null;
        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            var me = await _users.GetUserAsync(User);
            meId = me?.Id;
            scopeCompanyId = me?.TestingCompanyId;
            if (scopeCompanyId is null) return; // an admin not attached to a company owns no tests
            CompanyId = null; // ignore any attempt to widen the view via the query string
        }
        else
        {
            scopeCompanyId = CompanyId;
            CompanyOptions = await _db.TestingCompanies
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CompanyId))
                .ToListAsync();
        }

        if (scopeCompanyId is not null)
            query = query.Where(t => t.Tester != null && t.Tester.TestingCompanyId == scopeCompanyId);

        // Tester dropdown, narrowed to the scoped company when one applies.
        IQueryable<Tester> testerQuery = _db.Users;
        if (scopeCompanyId is not null)
            testerQuery = testerQuery.Where(u => u.TestingCompanyId == scopeCompanyId);
        TesterOptions = await testerQuery
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
            .Select(u => new SelectListItem(
                u.DisplayName != "" ? u.DisplayName : (u.Email ?? u.Id),
                u.Id,
                u.Id == TesterId))
            .ToListAsync();

        if (!string.IsNullOrEmpty(TesterId))
            query = query.Where(t => t.TesterId == TesterId);

        // Deep-link from a farm's details page: scope to that farm's tests. Resolve the heading's
        // farm name through the company scope for non-super-admins, so a guessed farm id can't
        // disclose another company's farm name.
        if (FarmId is not null)
        {
            query = query.Where(t => t.FarmId == FarmId);
            var farmQuery = _db.Farms.Where(f => f.Id == FarmId);
            if (!User.IsInRole(Roles.SuperAdministrator))
                farmQuery = farmQuery.InCompanyScope(_db, scopeCompanyId, meId);
            FarmName = await farmQuery.Select(f => f.Name).FirstOrDefaultAsync();
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
