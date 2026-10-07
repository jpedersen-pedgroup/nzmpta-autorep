namespace Autorep.Web.Services.Pdfs;

/// <summary>The kinds of PDF the server keeps, each in a container of its own
/// (<c>infra/modules/storage.bicep</c>).</summary>
public enum PdfContainer
{
    /// <summary>The Final Report as the tester signed it off (PRD: FinalReportBlob).</summary>
    FinalReports,

    /// <summary>The pulsation analyser PDFs testers attach to tests (O3).</summary>
    PulsationData,
}

/// <summary>What is held under a key: the SHA-256 of its bytes (lowercase hex), how many bytes,
/// and the content type it was stored with.</summary>
public sealed record StoredPdfInfo(string Sha256, long Length, string ContentType);

public sealed record StoredPdf(byte[] Bytes, StoredPdfInfo Info);

public enum PdfPutOutcome
{
    /// <summary>Nothing was held under the key before.</summary>
    Created,

    /// <summary>Different bytes were held under the key; these replaced them.</summary>
    Replaced,

    /// <summary>The same bytes were already held: nothing was written.</summary>
    Unchanged,
}

public sealed record PdfPutResult(string Sha256, PdfPutOutcome Outcome);

/// <summary>
/// Where the server keeps PDFs: Azure Blob Storage in Staging and Production, a folder in
/// Development, memory in the Testing environment (see <see cref="PdfStoreRegistration"/>). Keys
/// come from <see cref="PdfKeys"/> — deterministic, and under the owning tester's prefix.
///
/// Every object carries the SHA-256 of its bytes: putting the same bytes again is a no-op, and a
/// read that doesn't hash to what was stored is refused rather than handed on.
/// </summary>
public interface IPdfStore
{
    Task<PdfPutResult> PutAsync(PdfContainer container, string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default);

    /// <summary>The bytes under the key, or null when nothing is.</summary>
    Task<StoredPdf?> GetAsync(PdfContainer container, string key, CancellationToken ct = default);

    /// <summary>What is under the key without reading it, or null when nothing is.</summary>
    Task<StoredPdfInfo?> GetInfoAsync(PdfContainer container, string key, CancellationToken ct = default);

    /// <summary>Removes the key; false when nothing was there. Nothing in the app deletes a PDF —
    /// tests are never hard-deleted — so this is for operations and tests.</summary>
    Task<bool> DeleteAsync(PdfContainer container, string key, CancellationToken ct = default);
}

public static class PdfStoreExtensions
{
    public static async Task<bool> ExistsAsync(this IPdfStore store, PdfContainer container, string key, CancellationToken ct = default) =>
        await store.GetInfoAsync(container, key, ct) is not null;
}

/// <summary>The store couldn't do what was asked: unreachable, refused, or it handed back bytes
/// that don't match their hash. Callers treat it as "not now" — log it and answer 503, or carry on
/// without the PDF — never as "there is no PDF".</summary>
public sealed class PdfStoreException(string message, Exception? inner = null) : Exception(message, inner);
