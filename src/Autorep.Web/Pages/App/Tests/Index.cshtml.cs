using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.App.Tests;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public IList<MachineTest> Tests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Tests = await _db.MachineTests
            .Include(t => t.Farm)
            .Where(t => t.TesterId == testerId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
