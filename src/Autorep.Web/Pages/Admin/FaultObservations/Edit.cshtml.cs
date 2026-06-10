using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.FaultObservations;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    public EditModel(AutorepDbContext db) => _db = db;

    public static readonly string[] Severities = ["Critical", "Major", "Minor"];

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Severity { get; set; } = "Major";
    [BindProperty] public string? Recommendation { get; set; }
    public string Category { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var f = await _db.FaultObservations.FindAsync(Id);
        if (f is null) return NotFound();
        Category = f.Category; Name = f.Name; Severity = f.Severity; Recommendation = f.Recommendation; IsActive = f.IsActive;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var f = await _db.FaultObservations.FindAsync(Id);
        if (f is null) return NotFound();
        Category = f.Category; IsActive = f.IsActive;

        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Observation wording is required.");
        if (!Severities.Contains(Severity)) Errors.Add("Unknown severity.");
        if (await _db.FaultObservations.AnyAsync(x => x.Id != Id && x.Category == f.Category && x.Name == name))
            Errors.Add("That observation already exists for this check.");
        if (Errors.Count > 0) return Page();

        f.Name = name;
        f.Severity = Severity;
        f.Recommendation = string.IsNullOrWhiteSpace(Recommendation) ? null : Recommendation.Trim();
        f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        Message = "Saved. Devices receive the change on their next sync.";
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        var f = await _db.FaultObservations.FindAsync(Id);
        if (f is null) return NotFound();
        f.IsActive = !f.IsActive;
        f.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var f = await _db.FaultObservations.FindAsync(Id);
        if (f is null) return NotFound();
        _db.FaultObservations.Remove(f);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/FaultObservations/Index");
    }
}
