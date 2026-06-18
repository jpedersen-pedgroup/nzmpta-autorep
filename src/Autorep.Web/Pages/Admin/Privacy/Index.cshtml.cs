using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Privacy;

// Super-Admin editor for the privacy + terms content (folder gated "SuperAdminOnly" in Program.cs).
// Bumping the Terms version forces every tester to re-accept at next sign-in.
public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    public IndexModel(AutorepDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Saved { get; private set; }

    public class InputModel
    {
        public string TermsVersion { get; set; } = "";
        public string TermsBody { get; set; } = "";
        public string CollectionNotice { get; set; } = "";
        public string ReportFooterText { get; set; } = "";
        public string PrivacyContactEmail { get; set; } = "";
        public string PrivacyStatementUrl { get; set; } = "";
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var c = await _db.PrivacyContent.OrderByDescending(p => p.UpdatedAt).FirstOrDefaultAsync();
        if (c is null)
        {
            c = new PrivacyContent();
            _db.PrivacyContent.Add(c);
        }
        c.TermsVersion = (Input.TermsVersion ?? "").Trim();
        c.TermsBody = Input.TermsBody ?? "";
        c.CollectionNotice = Input.CollectionNotice ?? "";
        c.ReportFooterText = Input.ReportFooterText ?? "";
        c.PrivacyContactEmail = (Input.PrivacyContactEmail ?? "").Trim();
        c.PrivacyStatementUrl = (Input.PrivacyStatementUrl ?? "").Trim();
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        Saved = true;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var c = await _db.PrivacyContent.OrderByDescending(p => p.UpdatedAt).FirstOrDefaultAsync();
        if (c is not null)
        {
            Input = new InputModel
            {
                TermsVersion = c.TermsVersion,
                TermsBody = c.TermsBody,
                CollectionNotice = c.CollectionNotice,
                ReportFooterText = c.ReportFooterText,
                PrivacyContactEmail = c.PrivacyContactEmail,
                PrivacyStatementUrl = c.PrivacyStatementUrl,
            };
        }
    }
}
