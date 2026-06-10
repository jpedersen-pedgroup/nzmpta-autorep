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
    public record Group(string Type, string Title, IList<Row> Rows);

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
            .Select(t => new Group(t, TitleFor(t),
                rows.Where(e => e.Type == t).Select(e => new Row(e.Id, e.Name, e.Brand, e.IsActive)).ToList()))
            .ToList();
    }
}
