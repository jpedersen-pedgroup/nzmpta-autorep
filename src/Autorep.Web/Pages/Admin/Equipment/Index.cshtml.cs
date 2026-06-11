using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Equipment;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    public record Row(Guid Id, string Name, string? Brand, bool IsActive);
    public record BrandGroup(string Title, IList<Row> Rows);
    public record Group(string Type, string Title, IList<Row> Rows, IList<BrandGroup> Brands);

    public IList<Group> Groups { get; private set; } = [];

    public static string TitleFor(string type) => type switch
    {
        EquipmentItem.Shell => "Shells",
        EquipmentItem.Liner => "Liners",
        EquipmentItem.Pulsator => "Pulsator models",
        EquipmentItem.MilklineSize => "Milkline sizes (mm)",
        EquipmentItem.PulsatorConfiguration => "Pulsator configurations",
        _ => type,
    };

    public async Task OnGetAsync()
    {
        var rows = await _db.EquipmentItems
            .OrderBy(e => e.Brand).ThenBy(e => e.Name)
            .ToListAsync();
        Groups = EquipmentItem.Types
            .Select(t =>
            {
                var typeRows = rows.Where(e => e.Type == t).Select(e => new Row(e.Id, e.Name, e.Brand, e.IsActive)).ToList();
                return new Group(t, TitleFor(t), typeRows, BrandsFor(typeRows));
            })
            .ToList();
    }

    // Brand sub-groups for catalogs that carry brand data (pulsator models). Catalogs whose rows
    // have no brands at all render as a flat table instead.
    private static IList<BrandGroup> BrandsFor(IList<Row> rows) =>
        rows.Any(r => r.Brand is not null)
            ? rows.GroupBy(r => r.Brand ?? "(No brand)")
                .OrderBy(g => g.Key == "(No brand)")
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new BrandGroup(g.Key, g.ToList()))
                .ToList()
            : [];
}
