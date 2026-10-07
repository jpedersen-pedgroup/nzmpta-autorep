using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Admin.Tests;

// Hosts the wizard for an admin to view ANY test (Super-Admin) or their company's tests
// (Company-Admin), and to edit a completed one as its next version. The client fetches
// /api/tests/{id} and saves through /api/admin/tests/{id}/versions; authorization, company scoping
// and the role's field limits are enforced by those endpoints. Folder-gated AdminArea via Program.cs.
public class ViewModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public void OnGet() { }
}
