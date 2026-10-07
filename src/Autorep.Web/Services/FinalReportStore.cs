using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// Keeps a version's Final Report: the PDF in the PDF store's final-reports container, under the
/// tester and the version (<see cref="PdfKeys.FinalReport"/>), and its record in FinalReportBlobs
/// (written through EF, so every upload is audited). The server never generates a report — it keeps
/// what it was given: by the tester's device for a version they signed off (FinalReportsController),
/// or by the admin viewer for a version an administrator saved (AdminTestsController). Same bytes
/// again change nothing; different bytes replace the copy, logged, with blob versioning holding the
/// one replaced.
/// </summary>
public sealed class FinalReportStore(AutorepDbContext db, IPdfStore store, ILogger<FinalReportStore> log)
{
    /// <summary>
    /// The most a stored Final Report may be: the generated report (well under a megabyte) plus an
    /// analyser PDF (at most 15 MB on the device), with room for pdf-lib re-writing the merge.
    /// Mirrored as MAX_FINAL_REPORT_BYTES in Client/sync/finalReportUpload.ts.
    /// </summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    public abstract record Outcome;
    /// <summary>Kept: <paramref name="Status"/> is "created", "replaced" or "unchanged".</summary>
    public sealed record Stored(string Status, string Sha256, long SizeBytes) : Outcome;
    public sealed record NotPdf : Outcome;
    /// <summary>The PDF store couldn't be written: the sender tries again later.</summary>
    public sealed record Unavailable : Outcome;
    /// <summary>Another request stored the first copy at the same moment: try again.</summary>
    public sealed record Busy : Outcome;

    /// <summary>Keeps <paramref name="bytes"/> as the Final Report of the version
    /// <paramref name="machineTestId"/> (the tester's test <paramref name="clientId"/>), sent by
    /// <paramref name="storedBy"/>.</summary>
    public async Task<Outcome> StoreAsync(
        Guid machineTestId, string testerId, Guid clientId, byte[] bytes, string storedBy, CancellationToken ct)
    {
        if (!PdfHash.LooksLikePdf(bytes)) return new NotPdf();

        var sha = PdfHash.Sha256Hex(bytes);
        var key = PdfKeys.FinalReport(testerId, clientId);
        var record = await db.FinalReportBlobs.FirstOrDefaultAsync(r => r.MachineTestId == machineTestId, ct);

        try
        {
            // The same report again (a retry whose answer was lost): nothing to do, provided the
            // store still has it.
            if (record is not null && record.Sha256 == sha && record.BlobKey == key
                && (await store.GetInfoAsync(PdfContainer.FinalReports, key, ct))?.Sha256 == sha)
            {
                return new Stored("unchanged", sha, bytes.Length);
            }
            await store.PutAsync(PdfContainer.FinalReports, key, bytes, "application/pdf", ct);
        }
        catch (PdfStoreException e)
        {
            log.LogError(e, "Final Report upload for test {TestId} (client {ClientId}) could not be stored", machineTestId, clientId);
            return new Unavailable();
        }

        var created = record is null;
        if (record is null)
        {
            record = new FinalReportBlob { MachineTestId = machineTestId };
            db.FinalReportBlobs.Add(record);
        }
        else if (record.Sha256 != sha)
        {
            // The generator is deterministic for a given version, so a second, different report means
            // something it reads changed between attempts (the company logo, the privacy footer, the
            // standards). The newer copy is kept, as the PRD's "most recent upload" says; blob
            // versioning holds the one it replaced.
            log.LogWarning("Final Report for test {TestId} replaced with different bytes ({OldSha256} -> {NewSha256})",
                machineTestId, record.Sha256, sha);
        }
        record.BlobKey = key;
        record.Sha256 = sha;
        record.SizeBytes = bytes.Length;
        record.StoredAt = DateTimeOffset.UtcNow;
        record.StoredBy = storedBy;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (created)
        {
            // Two requests sent it at once and the other one's record landed first.
            return new Busy();
        }

        log.LogInformation("Final Report stored for test {TestId}: {SizeBytes} bytes, sha256 {Sha256}", machineTestId, bytes.Length, sha);
        return new Stored(created ? "created" : "replaced", sha, bytes.Length);
    }

    public static bool IsPdf(string? contentType) =>
        contentType is not null
        && contentType.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public sealed class BodyTooLargeException(long read) : Exception
    {
        public long Read { get; } = read;
    }

    /// <summary>The request body, refusing it past <see cref="MaxBytes"/> — whatever Content-Length
    /// claimed, and whether or not the server in front enforces the attribute's limit (the test
    /// server doesn't).</summary>
    public static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream(request.ContentLength is { } length and > 0 ? (int)length : 64 * 1024);
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
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
