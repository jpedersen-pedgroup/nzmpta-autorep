using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Tests;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public IList<MachineTest> Tests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Tests = await _db.MachineTests
            .Include(t => t.Tester)
            .Include(t => t.Farm)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
