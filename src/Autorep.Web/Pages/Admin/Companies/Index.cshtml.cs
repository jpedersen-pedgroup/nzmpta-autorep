using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Companies;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record CompanyRow(Guid Id, string Name, bool IsActive, int TesterCount, int TestCount, DateTimeOffset CreatedAt);

    public IList<CompanyRow> Companies { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Companies = await _db.TestingCompanies
            .OrderBy(c => c.Name)
            .Select(c => new CompanyRow(
                c.Id,
                c.Name,
                c.IsActive,
                c.Testers.Count,
                // Tests done for the company (the stamp on each test), so a tester who has moved
                // on doesn't take their history with them.
                _db.MachineTests.Count(t => t.TestingCompanyId == c.Id),
                c.CreatedAt))
            .ToListAsync();
    }
}
