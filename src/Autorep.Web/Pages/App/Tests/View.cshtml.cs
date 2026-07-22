using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.App.Tests;

// Hosts the read-only wizard for a tester to view a test from the Company tests list. The page
// itself renders for any id — authorization and company scoping are enforced by /api/tests/{id},
// which returns NotFound for anything out of scope. Folder-gated TesterArea via Program.cs.
public class ViewModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public void OnGet() { }
}
