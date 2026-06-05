using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Regions;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record Row(Guid Id, string Name, string Island, int SortOrder, bool IsActive, int FarmCount);
    public IList<Row> Regions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Regions = await _db.Regions
            .OrderBy(r => r.Island).ThenBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new Row(r.Id, r.Name, r.Island, r.SortOrder, r.IsActive,
                _db.Farms.Count(f => f.RegionId == r.Id)))
            .ToListAsync();
    }
}
