using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// The Final Report as the tester signed it off (PRD: FinalReportBlob). The device generates the
// full report at sign-off — every part, the analyser PDF appended — and sends it here with the next
// sync; administrators download it from /api/tests/{id}/final-report. The server never generates a
// report: it keeps what it was given (Services/FinalReportStore.cs).
[ApiController]
[Route("api/sync/final-report")]
[Authorize(Roles = Roles.Tester)]
public class FinalReportsController : ControllerBase
{
    /// <summary>See <see cref="FinalReportStore.MaxBytes"/>.</summary>
    public const long MaxBytes = FinalReportStore.MaxBytes;

    private readonly AutorepDbContext _db;
    private readonly FinalReportStore _reports;
    private readonly ILogger<FinalReportsController> _log;

    public FinalReportsController(AutorepDbContext db, FinalReportStore reports, ILogger<FinalReportsController> log)
    {
        _db = db;
        _reports = reports;
        _log = log;
    }

    public record StoredResponse(string Status, string Sha256, long SizeBytes);

    // The body is the PDF itself (application/pdf), not base64 inside JSON: a third smaller on the
    // wire, and nothing to decode. Only for the caller's own tests, and only once the server has the
    // test as complete — the report is of a signed-off test. Same bytes again: nothing changes.
    [HttpPut("{clientId:guid}")]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Put(Guid clientId, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");

        if (!FinalReportStore.IsPdf(Request.ContentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { error = "Send the report as application/pdf." });
        if (Request.ContentLength is > MaxBytes)
            return TooLarge(testerId, clientId, Request.ContentLength.Value);

        var test = await _db.MachineTests
            .Where(t => t.TesterId == testerId && t.ClientId == clientId)
            .Select(t => new { t.Id, t.MarkedCompleteAt })
            .FirstOrDefaultAsync(ct);
        if (test is null) return NotFound();
        if (test.MarkedCompleteAt is null)
            return Conflict(new { error = "The server doesn't have this test as complete yet — sync the test first." });

        byte[] bytes;
        try
        {
            bytes = await FinalReportStore.ReadBodyAsync(Request, ct);
        }
        catch (FinalReportStore.BodyTooLargeException e)
        {
            return TooLarge(testerId, clientId, e.Read);
        }

        return await _reports.StoreAsync(test.Id, testerId, clientId, bytes, testerId, ct) switch
        {
            FinalReportStore.Stored { Status: "created" } s => StatusCode(StatusCodes.Status201Created, new StoredResponse(s.Status, s.Sha256, s.SizeBytes)),
            FinalReportStore.Stored s => Ok(new StoredResponse(s.Status, s.Sha256, s.SizeBytes)),
            FinalReportStore.NotPdf => BadRequest(new { error = "That isn't a PDF." }),
            // Two of the tester's tabs sent it at once and the other one's record landed first. The
            // device tries again later and finds the record.
            FinalReportStore.Busy => Conflict(new { error = "The report was being stored by another request — try again." }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The report store is unavailable — the device will send it again later." }),
        };
    }

    private IActionResult TooLarge(string testerId, Guid clientId, long size)
    {
        _log.LogWarning("Final Report upload refused for tester {TesterId}, client {ClientId}: {SizeBytes} bytes is over the {MaxBytes} limit",
            testerId, clientId, size, MaxBytes);
        return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"A Final Report can be at most {MaxBytes / 1024 / 1024} MB." });
    }
}
