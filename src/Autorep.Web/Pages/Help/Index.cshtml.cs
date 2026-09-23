using System.Globalization;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Help;

// Help & guides: the work-instruction PDFs this account's role can open (Guides/guides.json).
// Every signed-in role reaches it; GuidesController enforces the same roles on the files.
public class IndexModel : PageModel
{
    private readonly GuideCatalog _catalog;
    public IndexModel(GuideCatalog catalog) => _catalog = catalog;

    public IReadOnlyList<Guide> Guides { get; private set; } = [];

    public void OnGet() => Guides = _catalog.VisibleTo(User);

    /// <summary>"3.5 MB" / "850 KB" — the download a tester on farm data is about to make.</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
}
