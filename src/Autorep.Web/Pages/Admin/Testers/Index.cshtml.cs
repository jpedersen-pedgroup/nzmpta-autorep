using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Testers;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;

    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    public record TesterRow(
        string Id,
        string Email,
        string DisplayName,
        string? CompanyName,
        string RolesDisplay,
        bool IsActive,
        DateOnly? LicenceExpiryDate);

    public IList<TesterRow> Testers { get; private set; } = [];

    /// <summary>A Tester or Company Administrator with no Testing Company, which the forms no longer
    /// allow but older accounts (or migrated ones) may have. Listed whatever the company filter.</summary>
    public record MissingCompanyRow(string Id, string Email, string Roles);
    public IList<MissingCompanyRow> MissingCompany { get; private set; } = [];

    // Company dropdown filter; also the deep-link target from the Companies
    // list's tester counts (/Admin/Testers?companyId=...).
    [BindProperty(SupportsGet = true)] public Guid? CompanyId { get; set; }
    public List<SelectListItem> CompanyOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        CompanyOptions = await _db.TestingCompanies
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CompanyId))
            .ToListAsync();

        IQueryable<Tester> usersQuery = _db.Users.Include(u => u.TestingCompany);
        if (CompanyId is not null)
            usersQuery = usersQuery.Where(u => u.TestingCompanyId == CompanyId);

        var users = await usersQuery
            .OrderBy(u => u.Email)
            .ToListAsync();

        var rows = new List<TesterRow>(users.Count);
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            var rolesDisplay = roles.Count == 0 ? "—" : string.Join(", ", roles.Select(Roles.Label));
            var isActive = !u.LockoutEnd.HasValue || u.LockoutEnd < DateTimeOffset.UtcNow;
            rows.Add(new TesterRow(
                u.Id,
                u.Email ?? "",
                u.DisplayName,
                u.TestingCompany?.Name,
                rolesDisplay,
                isActive,
                u.LicenceExpiryDate));
        }
        Testers = rows;

        var needCompany = new[] { Roles.Tester, Roles.CompanyAdministrator };
        var missing = await (
            from u in _db.Users
            where u.TestingCompanyId == null
            join ur in _db.UserRoles on u.Id equals ur.UserId
            join r in _db.Roles on ur.RoleId equals r.Id
            where needCompany.Contains(r.Name!)
            select new { u.Id, u.Email, Role = r.Name! }).ToListAsync();
        MissingCompany = missing
            .GroupBy(m => (m.Id, m.Email))
            .OrderBy(g => g.Key.Email)
            .Select(g => new MissingCompanyRow(g.Key.Id, g.Key.Email ?? "",
                string.Join(", ", g.Select(m => m.Role).Order().Select(Roles.Label))))
            .ToList();
    }
}
