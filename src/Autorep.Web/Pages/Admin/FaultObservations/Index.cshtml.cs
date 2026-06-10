using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.FaultObservations;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record Row(Guid Id, string Name, string Severity, string? Recommendation, bool IsActive);
    public record Group(string Category, IList<Row> Rows);

    public IList<Group> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var rows = await _db.FaultObservations
            .OrderBy(f => f.Category).ThenBy(f => f.Name)
            .ToListAsync();
        Groups = rows
            .GroupBy(f => f.Category)
            .Select(g => new Group(g.Key,
                g.Select(f => new Row(f.Id, f.Name, f.Severity, f.Recommendation, f.IsActive)).ToList()))
            .ToList();
    }
}
