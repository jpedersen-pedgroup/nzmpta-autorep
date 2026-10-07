using System.Collections.Concurrent;

namespace Autorep.Web.Services.Pdfs;

/// <summary>
/// The store in the Testing environment — xUnit and the Playwright suite — so nothing under test
/// needs Azure. Lives as long as the host. <see cref="Unavailable"/> makes every call fail the way
/// an unreachable Blob account does, for the "store is down" cases.
/// </summary>
public sealed class InMemoryPdfStore : IPdfStore
{
    private readonly ConcurrentDictionary<(PdfContainer, string), StoredPdf> _objects = new();

    /// <summary>While set, every call throws <see cref="PdfStoreException"/>.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Every key held in a container, for assertions.</summary>
    public IReadOnlyList<string> Keys(PdfContainer container) =>
        _objects.Keys.Where(k => k.Item1 == container).Select(k => k.Item2).Order(StringComparer.Ordinal).ToList();

    public Task<PdfPutResult> PutAsync(PdfContainer container, string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        PdfKeys.Checked(key);
        var sha = PdfHash.Sha256Hex(bytes.Span);
        var outcome = PdfPutOutcome.Created;
        _objects.AddOrUpdate((container, key),
            _ => new StoredPdf(bytes.ToArray(), new StoredPdfInfo(sha, bytes.Length, contentType)),
            (_, existing) =>
            {
                if (existing.Info.Sha256 == sha)
                {
                    outcome = PdfPutOutcome.Unchanged;
                    return existing;
                }
                outcome = PdfPutOutcome.Replaced;
                return new StoredPdf(bytes.ToArray(), new StoredPdfInfo(sha, bytes.Length, contentType));
            });
        return Task.FromResult(new PdfPutResult(sha, outcome));
    }

    public Task<StoredPdf?> GetAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        // A copy, so a caller can't change what is "stored".
        return Task.FromResult(_objects.TryGetValue((container, PdfKeys.Checked(key)), out var pdf)
            ? new StoredPdf(pdf.Bytes.ToArray(), pdf.Info)
            : null);
    }

    public Task<StoredPdfInfo?> GetInfoAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(_objects.TryGetValue((container, PdfKeys.Checked(key)), out var pdf) ? pdf.Info : null);
    }

    public Task<bool> DeleteAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(_objects.TryRemove((container, PdfKeys.Checked(key)), out _));
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable) throw new PdfStoreException("The PDF store is unavailable (InMemoryPdfStore.Unavailable).");
    }
}
