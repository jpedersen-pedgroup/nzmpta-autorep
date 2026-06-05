using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.MilkSupplyCompanies;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record Row(Guid Id, string Name, bool IsActive, int FarmCount, DateTimeOffset CreatedAt);
    public IList<Row> Companies { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Companies = await _db.MilkSupplyCompanies
            .OrderBy(c => c.Name)
            .Select(c => new Row(c.Id, c.Name, c.IsActive,
                _db.Farms.Count(f => f.MilkSupplyCompanyId == c.Id), c.CreatedAt))
            .ToListAsync();
    }
}
