using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// Keeps the pulsation analyser PDFs (O3) in the PDF store, out of <c>MachineTest.PayloadJson</c>:
/// base64 inside the payload made every PDF a third bigger, and was carried by every backup, every
/// pull and every read of a test. The stored payload holds a pointer instead — the shape the device
/// already understands (<c>onServer</c>, <c>serverTestId</c>), plus the <c>sha256</c> that names the
/// object (see <see cref="PulsationPayload"/>). The bytes live at
/// <c>{testerId}/{holder clientId}/pulsation/{sha256}.pdf</c>: under the test that first sent them,
/// so a new version made from it points at the same object rather than copying it.
///
/// The store failing never fails a sync: an incoming PDF then stays inline, as before, and the
/// backfill (PulsationBackfill) moves it later.
/// </summary>
public sealed class PulsationAttachments
{
    private readonly AutorepDbContext _db;
    private readonly IPdfStore _store;
    private readonly ILogger<PulsationAttachments> _log;

    public PulsationAttachments(AutorepDbContext db, IPdfStore store, ILogger<PulsationAttachments> log)
    {
        _db = db;
        _store = store;
        _log = log;
    }

    /// <summary>
    /// The payload to keep for a push. Bytes sent inline go to the store, leaving a pointer. A
    /// pointer — a device that dropped its copy — is matched to a stored copy of the SAME attachment
    /// (name, size, attach time) on the test it names, this test, or the version it supersedes, and
    /// takes that copy's hash and holder; only ever the caller's own tests. A device holding a stale
    /// pointer (another device has since attached a different PDF) must never get the newer PDF's
    /// bytes under the old one's name, so with no match the pointer is kept as sent, minus any hash:
    /// the report prints without the PDF rather than with the wrong one.
    /// </summary>
    public async Task<string?> StoreIncomingAsync(string? incoming, string testerId, Guid clientId, Guid? supersedesClientId, CancellationToken ct)
    {
        if (PulsationPayload.Base64(incoming) is { } inline)
            return await MoveInlineAsync(incoming!, inline, testerId, clientId, ct) ?? incoming;

        if (!PulsationPayload.IsServerPointer(incoming, out var source)) return incoming;

        var candidates = new List<Guid> { source ?? clientId, clientId };
        if (supersedesClientId is { } previous) candidates.Add(previous);
        foreach (var id in candidates.Distinct())
        {
            var stored = await _db.MachineTests
                .Where(t => t.TesterId == testerId && t.ClientId == id)
                .Select(t => t.PayloadJson)
                .FirstOrDefaultAsync(ct);
            if (!PulsationPayload.SameAttachment(stored, incoming)) continue;

            if (PulsationPayload.StoredSha256(stored) is { } sha)
            {
                var holder = PulsationPayload.HolderClientId(stored) ?? id;
                return PulsationPayload.AsStored(incoming, sha, holder == clientId ? null : holder);
            }
            // A copy still inline in its row (written before the store, or while it was down).
            if (PulsationPayload.Base64(stored) is { } bytes)
                return await MoveInlineAsync(incoming!, bytes, testerId, clientId, ct) ?? PulsationPayload.WithBytes(incoming!, bytes);
        }
        return PulsationPayload.WithoutStoredHash(incoming);
    }

    /// <summary>Puts inline bytes in the store under this test and gives the pointer payload — or
    /// null when they can't go there now (not base64, or the store is down), leaving the caller to
    /// keep them inline.</summary>
    private async Task<string?> MoveInlineAsync(string payload, string base64, string testerId, Guid clientId, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null; // not ours to "fix": the device's payload is kept as it is
        }
        try
        {
            var put = await _store.PutAsync(PdfContainer.PulsationData, PdfKeys.Pulsation(testerId, clientId, PdfHash.Sha256Hex(bytes)), bytes, "application/pdf", ct);
            return PulsationPayload.AsStored(payload, put.Sha256, holderClientId: null);
        }
        catch (PdfStoreException e)
        {
            _log.LogWarning(e, "Pulsation PDF for client {ClientId} kept inline: the PDF store is unavailable", clientId);
            return null;
        }
    }

    /// <summary>
    /// The attachment's bytes for a test row: from the store when the payload points there, else
    /// from bytes still inline. Null when there are none — no attachment, a pointer that names no
    /// stored copy, or a stored object gone missing. Throws <see cref="PdfStoreException"/> when the
    /// store can't be read, which is "not now", not "none".
    /// </summary>
    public async Task<byte[]?> BytesAsync(string testerId, Guid clientId, string? payloadJson, CancellationToken ct)
    {
        if (PulsationPayload.Base64(payloadJson) is { } inline)
        {
            try
            {
                return Convert.FromBase64String(inline);
            }
            catch (FormatException)
            {
                return null;
            }
        }
        if (PulsationPayload.StoredSha256(payloadJson) is not { } sha) return null;

        var key = PdfKeys.Pulsation(testerId, PulsationPayload.HolderClientId(payloadJson) ?? clientId, sha);
        var pdf = await _store.GetAsync(PdfContainer.PulsationData, key, ct);
        if (pdf is null)
        {
            _log.LogError("Pulsation PDF {BlobKey} is named by test {ClientId}'s payload but isn't in the store", key, clientId);
            return null;
        }
        if (pdf.Info.Sha256 != sha)
        {
            // The key IS the hash, so this is a damaged object, not a newer one.
            _log.LogError("Pulsation PDF {BlobKey} doesn't match the hash it is stored under", key);
            return null;
        }
        return pdf.Bytes;
    }

    /// <summary>
    /// The payload with the bytes back inline — for a device that didn't ask for
    /// <c>attachments=omit</c>, which has always been sent them (anything older keeps working).
    /// If they can't be read right now the pointer goes as it is, and the device's report says the
    /// analyser PDF is on the server.
    /// </summary>
    public async Task<string?> RehydrateAsync(string? payloadJson, string testerId, Guid clientId, CancellationToken ct)
    {
        if (PulsationPayload.StoredSha256(payloadJson) is null) return payloadJson;
        try
        {
            return await BytesAsync(testerId, clientId, payloadJson, ct) is { } bytes
                ? PulsationPayload.WithBytes(payloadJson!, Convert.ToBase64String(bytes))
                : payloadJson;
        }
        catch (PdfStoreException e)
        {
            _log.LogWarning(e, "Pulsation PDF for client {ClientId} not put back into a pull: the PDF store is unavailable", clientId);
            return payloadJson;
        }
    }

    /// <summary>The key under which a row's own inline bytes would be stored. Rows always have a
    /// ClientId when they came through sync; the server id stands in for one that didn't.</summary>
    public static Guid ClientIdOf(MachineTest row) => row.ClientId ?? row.Id;
}
