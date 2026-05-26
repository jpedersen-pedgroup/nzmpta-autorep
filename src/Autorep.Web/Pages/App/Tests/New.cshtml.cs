using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.App.Tests;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;

    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<string> Errors { get; } = new();

    public class InputModel
    {
        public string FarmName { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool MarkComplete { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.FarmName))
        {
            Errors.Add("Farm name is required.");
            return Page();
        }

        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Missing NameIdentifier claim.");

        var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Name == Input.FarmName);
        if (farm is null)
        {
            farm = new Farm { Name = Input.FarmName };
            _db.Farms.Add(farm);
        }

        var test = new MachineTest
        {
            TesterId = testerId,
            FarmId = farm.Id,
            Farm = farm,
            Notes = Input.Notes,
            MarkedCompleteAt = Input.MarkComplete ? DateTimeOffset.UtcNow : null
        };
        _db.MachineTests.Add(test);
        await _db.SaveChangesAsync();

        return RedirectToPage("/App/Tests/Index");
    }
}
