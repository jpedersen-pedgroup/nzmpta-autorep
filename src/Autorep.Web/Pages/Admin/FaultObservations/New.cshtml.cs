using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.FaultObservations;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public string Category { get; set; } = string.Empty;
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Severity { get; set; } = "Major";
    [BindProperty] public string? Recommendation { get; set; }
    public IList<string> Categories { get; private set; } = [];
    public List<string> Errors { get; } = new();

    public async Task OnGetAsync() => Categories = await LoadCategoriesAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var category = Category.Trim();
        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(category)) Errors.Add("Check (category) is required.");
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Observation wording is required.");
        if (!EditModel.Severities.Contains(Severity)) Errors.Add("Unknown severity.");
        if (Errors.Count == 0 && await _db.FaultObservations.AnyAsync(x => x.Category == category && x.Name == name))
            Errors.Add("That observation already exists for this check.");
        if (Errors.Count > 0)
        {
            Categories = await LoadCategoriesAsync();
            return Page();
        }

        _db.FaultObservations.Add(new FaultObservation
        {
            Category = category,
            Name = name,
            Severity = Severity,
            Recommendation = string.IsNullOrWhiteSpace(Recommendation) ? null : Recommendation.Trim(),
        });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/FaultObservations/Index");
    }

    private async Task<IList<string>> LoadCategoriesAsync() =>
        await _db.FaultObservations.Select(f => f.Category).Distinct().OrderBy(c => c).ToListAsync();
}
