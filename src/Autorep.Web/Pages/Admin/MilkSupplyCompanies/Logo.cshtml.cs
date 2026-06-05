using Autorep.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Admin.MilkSupplyCompanies;

// Streams a milk-supply company's logo from the database. Admin-only (folder is
// authorised for Super-Administrators). Referenced by <img src=".../Logo/{id}">.
public class LogoModel : PageModel
{
    private readonly AutorepDbContext _db;
    public LogoModel(AutorepDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(id);
        if (c?.LogoData is null || c.LogoData.Length == 0) return NotFound();
        return File(c.LogoData, c.LogoContentType ?? "application/octet-stream");
    }
}
