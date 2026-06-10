using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Standards;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record Row(Guid Id, string Key, string Label, string Category, string Summary,
        string? Unit, string? SourceRef, DateTimeOffset UpdatedAt);

    public IList<IGrouping<string, Row>> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var rows = await _db.TestStandards
            .OrderBy(s => s.Category).ThenBy(s => s.Label)
            .ToListAsync();
        Groups = rows
            .Select(s => new Row(s.Id, s.Key, s.Label, s.Category, Summarise(s), s.Unit, s.SourceRef, s.UpdatedAt))
            .GroupBy(r => r.Category)
            .ToList();
    }

    /// <summary>Human-readable rendering of the rule/parameter, e.g. "≤ 1 kPa", "± 2 kPa", "4–12".</summary>
    public static string Summarise(TestStandard s) => s.Kind switch
    {
        "atMost" => $"≤ {s.Limit} {s.Unit}".Trim(),
        "atLeast" => $"≥ {s.Min} {s.Unit}".Trim(),
        "between" => $"{s.Min}–{s.Max} {s.Unit}".Trim(),
        "tolerance" => $"± {s.Tolerance} {s.Unit}".Trim(),
        "param" => $"{s.Value} {s.Unit}".Trim(),
        _ => "—",
    };
}
