using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Standards;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    public EditModel(AutorepDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public StandardInput Input { get; set; } = new();
    public string Key { get; private set; } = string.Empty;
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var s = await _db.TestStandards.FindAsync(Id);
        if (s is null) return NotFound();
        Key = s.Key;
        Input = StandardInput.From(s);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var s = await _db.TestStandards.FindAsync(Id);
        if (s is null) return NotFound();
        Key = s.Key;
        if (!Input.Validate(Errors)) return Page();
        Input.Apply(s);
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        Message = "Saved. Devices receive the new value on their next sync.";
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var s = await _db.TestStandards.FindAsync(Id);
        if (s is null) return NotFound();
        _db.TestStandards.Remove(s);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Standards/Index");
    }
}

/// <summary>Shared edit/new form payload + validation for a TestStandard.</summary>
public class StandardInput
{
    public string Label { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Kind { get; set; } = "atMost";
    public double? Limit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Target { get; set; }
    public double? Tolerance { get; set; }
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public string? SourceRef { get; set; }

    public static readonly string[] Kinds = ["atMost", "atLeast", "between", "tolerance", "param"];

    public static StandardInput From(TestStandard s) => new()
    {
        Label = s.Label, Category = s.Category, Kind = s.Kind,
        Limit = s.Limit, Min = s.Min, Max = s.Max, Target = s.Target, Tolerance = s.Tolerance,
        Value = s.Value, Unit = s.Unit, SourceRef = s.SourceRef,
    };

    public bool Validate(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Label)) errors.Add("Label is required.");
        if (!Kinds.Contains(Kind)) errors.Add("Unknown kind.");
        switch (Kind)
        {
            case "atMost" when Limit is null: errors.Add("A maximum (limit) value is required."); break;
            case "atLeast" when Min is null: errors.Add("A minimum value is required."); break;
            case "between" when Min is null || Max is null: errors.Add("Both minimum and maximum are required."); break;
            case "between" when Min > Max: errors.Add("Minimum must not exceed maximum."); break;
            case "tolerance" when Tolerance is null: errors.Add("A tolerance value is required."); break;
            case "param" when Value is null: errors.Add("A value is required."); break;
        }
        return errors.Count == 0;
    }

    public void Apply(TestStandard s)
    {
        s.Label = Label.Trim();
        s.Category = Category.Trim();
        s.Kind = Kind;
        s.Limit = Kind == "atMost" ? Limit : null;
        s.Min = Kind is "atLeast" or "between" ? Min : null;
        s.Max = Kind == "between" ? Max : null;
        s.Target = Kind == "tolerance" ? Target ?? 0 : null;
        s.Tolerance = Kind == "tolerance" ? Tolerance : null;
        s.Value = Kind == "param" ? Value : null;
        s.Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
        s.SourceRef = string.IsNullOrWhiteSpace(SourceRef) ? null : SourceRef.Trim();
    }
}
