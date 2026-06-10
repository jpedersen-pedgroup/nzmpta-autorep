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

    // ------------------------------------------------------------------------------------------
    // ISO / manual-fixed standards — shown read-only for reference. These mirror the values
    // compiled into the app (Client/passfail/standards.ts + Client/reference/lookups.ts) and are
    // fixed by the documents themselves, so they're intentionally not editable here.
    // ------------------------------------------------------------------------------------------

    public record FixedStandard(string Label, string Value, string Source);

    public static readonly FixedStandard[] FixedStandards =
    [
        new("Effective reserve — above 80 clusters", "2100 + 25 L/min per cluster over 80", "Manual p42"),
        new("Effective reserve — odd cluster counts", "round up to the next even table row", "Manual p42 (table steps by 2)"),
        new("Cleaning reserve (flushing / wash-injection systems)",
            "CR = π/4 × d² × 8 × ((100 − v) ÷ 100) × 0.06 — d = milkline internal Ø (OD − 2 mm), v = working vacuum rounded up; requirement = the higher of CR and the effective reserve",
            "Manual p43"),
        new("Atmospheric correction — application",
            "measured airflow (effective reserve, pump capacity) × factor, then compared to the unchanged standard",
            "Manual p31 / ISO 6690 §5.3.2"),
        new("ACR / milk-meter allowance — rounding", "round the total up to the nearest 10 L/min", "Manual p41"),
        new("ACR / milk-meter allowance — bail gates", "allowance doubled when bail-gate rams are present", "Manual p41"),
        new("Pulsator consumption — structure", "allowance per started block of 10 units", "Manual p41"),
        new("Vacuum pump capacity / speed", "per OEM performance curve and speed range (model lookup — pending)", "Manual pp8–30, 60"),
    ];

    /// <summary>Manual p42 effective-reserve table (even cluster counts; 1 is the 2-cluster row).</summary>
    public static IEnumerable<(int Clusters, int Reserve)> EffectiveReserveRows()
    {
        int[] reserves =
        [
            260, 320, 380, 440, 500, 520, 540, 560, 580, 600,
            650, 700, 750, 800, 850, 900, 950, 1000, 1050, 1100,
            1150, 1200, 1250, 1300, 1350, 1400, 1450, 1500, 1550, 1600,
            1650, 1700, 1750, 1800, 1850, 1900, 1950, 2000, 2050, 2100,
        ];
        for (var i = 0; i < reserves.Length; i++) yield return ((i + 1) * 2, reserves[i]);
    }

    /// <summary>Manual p31 / ISO Table 4 atmospheric correction factors.</summary>
    public static readonly (int Kpa, double Factor)[] AtmosFactors =
    [
        (90, 1.16), (91, 1.14), (92, 1.12), (93, 1.10), (94, 1.09), (95, 1.07), (96, 1.05), (97, 1.04),
        (98, 1.03), (99, 1.01), (100, 1.00), (101, 0.99), (102, 0.97), (103, 0.96), (104, 0.95), (105, 0.94),
    ];
}
