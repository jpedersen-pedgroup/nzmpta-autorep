using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Admin.Tests;

// Hosts the read-only wizard for an admin to view ANY test (Super-Admin) or their company's tests
// (Company-Admin). The client fetches /api/tests/{id} and renders it read-only; authorization +
// company scoping are enforced by that endpoint. Folder-gated AdminArea via Program.cs.
public class ViewModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public void OnGet() { }
}
