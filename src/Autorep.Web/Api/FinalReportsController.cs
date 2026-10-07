using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// The Final Report as the tester signed it off (PRD: FinalReportBlob). The device generates the
// full report at sign-off — every part, the analyser PDF appended — and sends it here with the next
// sync; administrators download it from /api/tests/{id}/final-report. The server never generates a
// report: it keeps what it was given.
[ApiController]
[Route("api/sync/final-report")]
[Authorize(Roles = Roles.Tester)]
public class FinalReportsController : ControllerBase
{
    /// <summary>
    /// The most a stored Final Report may be: the generated report (well under a megabyte) plus an
    /// analyser PDF (at most 15 MB on the device), with room for pdf-lib re-writing the merge.
    /// Mirrored as MAX_FINAL_REPORT_BYTES in Client/sync/finalReportUpload.ts.
    /// </summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    private readonly AutorepDbContext _db;
    private readonly IPdfStore _store;
    private readonly ILogger<FinalReportsController> _log;

    public FinalReportsController(AutorepDbContext db, IPdfStore store, ILogger<FinalReportsController> log)
    {
        _db = db;
        _store = store;
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

        if (!IsPdf(Request.ContentType))
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
            bytes = await ReadBodyAsync(ct);
        }
        catch (BodyTooLargeException e)
        {
            return TooLarge(testerId, clientId, e.Read);
        }
        if (!PdfHash.LooksLikePdf(bytes))
            return BadRequest(new { error = "That isn't a PDF." });

        var sha = PdfHash.Sha256Hex(bytes);
        var key = PdfKeys.FinalReport(testerId, clientId);
        var record = await _db.FinalReportBlobs.FirstOrDefaultAsync(r => r.MachineTestId == test.Id, ct);

        try
        {
            // The same report again (a retry whose answer was lost): nothing to do, provided the
            // store still has it.
            if (record is not null && record.Sha256 == sha && record.BlobKey == key
                && (await _store.GetInfoAsync(PdfContainer.FinalReports, key, ct))?.Sha256 == sha)
            {
                return Ok(new StoredResponse("unchanged", sha, bytes.Length));
            }
            await _store.PutAsync(PdfContainer.FinalReports, key, bytes, "application/pdf", ct);
        }
        catch (PdfStoreException e)
        {
            _log.LogError(e, "Final Report upload for test {TestId} (client {ClientId}) could not be stored", test.Id, clientId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The report store is unavailable — the device will send it again later." });
        }

        var created = record is null;
        if (record is null)
        {
            record = new FinalReportBlob { MachineTestId = test.Id };
            _db.FinalReportBlobs.Add(record);
        }
        else if (record.Sha256 != sha)
        {
            // The device's generator is deterministic for a signed-off test, so a second, different
            // report means something it reads changed between attempts (the company logo, the
            // privacy footer, the standards). The newer copy is kept, as the PRD's "most recent
            // upload" says; blob versioning holds the one it replaced.
            _log.LogWarning("Final Report for test {TestId} replaced with different bytes ({OldSha256} -> {NewSha256})",
                test.Id, record.Sha256, sha);
        }
        record.BlobKey = key;
        record.Sha256 = sha;
        record.SizeBytes = bytes.Length;
        record.StoredAt = DateTimeOffset.UtcNow;
        record.StoredBy = testerId;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (created)
        {
            // Two of the tester's tabs sent it at once and the other one's record landed first. The
            // device tries again later and finds the record.
            return Conflict(new { error = "The report was being stored by another request — try again." });
        }

        _log.LogInformation("Final Report stored for test {TestId}: {SizeBytes} bytes, sha256 {Sha256}", test.Id, bytes.Length, sha);
        var body = new StoredResponse(created ? "created" : "replaced", sha, bytes.Length);
        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    private IActionResult TooLarge(string testerId, Guid clientId, long size)
    {
        _log.LogWarning("Final Report upload refused for tester {TesterId}, client {ClientId}: {SizeBytes} bytes is over the {MaxBytes} limit",
            testerId, clientId, size, MaxBytes);
        return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"A Final Report can be at most {MaxBytes / 1024 / 1024} MB." });
    }

    private static bool IsPdf(string? contentType) =>
        contentType is not null
        && contentType.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    private sealed class BodyTooLargeException(long read) : Exception
    {
        public long Read { get; } = read;
    }

    /// <summary>The request body, refusing it past <see cref="MaxBytes"/> — whatever Content-Length
    /// claimed, and whether or not the server in front enforces the attribute's limit (the test
    /// server doesn't).</summary>
    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream(Request.ContentLength is { } length and > 0 ? (int)length : 64 * 1024);
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes) throw new BodyTooLargeException(buffer.Length + read);
                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new BodyTooLargeException(MaxBytes + 1);
        }
        return buffer.ToArray();
    }
}
