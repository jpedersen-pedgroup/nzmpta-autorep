using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.App.Tests;

// The New-test page is drawn on the device from the cached farm book (Client/ui/NewTestApp.tsx);
// see New.cshtml. Starting a test is a client-side navigation to the wizard, and the server's
// scope check now happens where data actually crosses: GET /api/farms/{id} (the wizard's farm
// snapshot) and the sync push (SyncController.ResolveFarmAsync). Adding a farm is POST /api/farms.
public class NewModel : PageModel { }
