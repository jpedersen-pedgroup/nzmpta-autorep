using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Autorep.Web.Api;

// The work-instruction PDFs behind the Help & guides page. Any signed-in account may ask; each
// guide's roles in Guides/guides.json decide the answer (a Tester asking for an admin guide gets
// 403). Deliberately NOT under /api: the PDFs are opened as page navigations, so an expired session
// should redirect to sign-in like a page does, and the service worker leaves /api/* alone — this
// route is the one it keeps offline copies of.
[Route("guides")]
[Authorize]
public class GuidesController : ControllerBase
{
    private readonly GuideCatalog _catalog;

    public GuidesController(GuideCatalog catalog) => _catalog = catalog;

    [HttpGet("{file}")]
    public IActionResult Get(string file)
    {
        var guide = _catalog.Find(file);
        if (guide is null) return NotFound();
        if (!guide.IsVisibleTo(User)) return Forbid();

        // Inline so it opens in the browser's / device's PDF viewer; the file name is what a save
        // from that viewer suggests.
        Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("inline")
        {
            FileName = $"NZMPTA AutoRep {guide.Title} v{guide.Version}.pdf"
        }.ToString();
        // Private: an admin guide must not sit in a shared proxy. no-cache: always revalidate, which
        // the ETag below turns into a cheap 304 — that is how a new version reaches devices.
        Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
        // Read back by the service worker to title its offline links (it only has the URL otherwise).
        Response.Headers["X-Guide-Title"] = guide.Title;

        // No range processing: the service worker can only keep whole (200) responses, and these
        // files are small enough that viewers don't need ranges.
        return PhysicalFile(_catalog.PhysicalPath(guide), "application/pdf",
            lastModified: null, entityTag: new EntityTagHeaderValue($"\"{guide.Hash}\""));
    }
}
