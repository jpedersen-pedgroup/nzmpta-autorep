using Autorep.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-side of the admin-managed privacy content: the tester client pulls this (and caches it in
// IndexedDB) so the in-app collection notice + the report privacy footer work offline. Editing
// happens in /Admin/Privacy (SuperAdmin). The TermsBody is intentionally NOT exposed here — terms
// acceptance is a server-rendered gate, not a client surface.
[ApiController]
[Route("api/privacy")]
[Authorize]
public class PrivacyController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public PrivacyController(AutorepDbContext db) => _db = db;

    public record PrivacyDto(
        string TermsVersion, string CollectionNotice, string ReportFooterText,
        string PrivacyContactEmail, string PrivacyStatementUrl);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var c = await _db.PrivacyContent.OrderByDescending(p => p.UpdatedAt).FirstOrDefaultAsync(ct);
        if (c is null) return Ok(new { version = (string?)null, content = (PrivacyDto?)null });

        return Ok(new
        {
            version = c.UpdatedAt.ToString("o"),
            content = new PrivacyDto(
                c.TermsVersion, c.CollectionNotice, c.ReportFooterText,
                c.PrivacyContactEmail, c.PrivacyStatementUrl),
        });
    }
}
