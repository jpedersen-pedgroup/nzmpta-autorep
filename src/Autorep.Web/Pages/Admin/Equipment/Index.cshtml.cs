using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Equipment;

public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    /// <summary>One item. <see cref="Spec"/> is the OEM spec in effect today in a line, or null when it
    /// has none; <see cref="NextChange"/> the date of the next scheduled version, if any.</summary>
    public record Row(Guid Id, string Name, string? Brand, bool IsActive, string? Spec, DateOnly? NextChange = null);
    public record BrandGroup(string Title, IList<Row> Rows);
    public record Group(string Type, string Title, IList<Row> Rows, IList<BrandGroup> Brands)
    {
        public bool HasSpec => OemSpecs.HasSpec(Type);
        /// <summary>Active items with no spec: the models testers can pick that get no spec verdict.</summary>
        public int ActiveWithoutSpec => HasSpec ? Rows.Count(r => r.IsActive && r.Spec is null) : 0;
    }

    public IList<Group> Groups { get; private set; } = [];

    public static string TitleFor(string type) => type switch
    {
        EquipmentItem.Shell => "Shells",
        EquipmentItem.Liner => "Liners",
        EquipmentItem.Pulsator => "Pulsator models",
        EquipmentItem.MilklineSize => "Milkline sizes (mm)",
        EquipmentItem.PulsatorConfiguration => "Pulsator configurations",
        EquipmentItem.VacuumPump => "Vacuum pumps",
        EquipmentItem.ReleaserPump => "Releaser pumps",
        EquipmentItem.Regulator => "Regulator types",
        _ => type,
    };

    public async Task OnGetAsync()
    {
        var rows = await _db.EquipmentItems
            .Include(e => e.SpecVersions)
            .OrderBy(e => e.Brand).ThenBy(e => e.Name)
            .ToListAsync();
        var today = NzTime.Today;
        Groups = EquipmentItem.Types
            .Select(t =>
            {
                var typeRows = rows.Where(e => e.Type == t)
                    .Select(e => new Row(e.Id, e.Name, e.Brand, e.IsActive,
                        SpecInput.Summary(e.Type, OemSpecs.InEffect(e.SpecVersions, today)?.SpecJson),
                        e.SpecVersions.Where(v => v.EffectiveFrom > today).Min(v => v.EffectiveFrom)))
                    .ToList();
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
