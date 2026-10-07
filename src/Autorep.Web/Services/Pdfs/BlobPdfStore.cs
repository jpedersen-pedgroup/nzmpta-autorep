using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Autorep.Web.Services.Pdfs;

/// <summary>
/// The store in Staging and Production: the storage account <c>infra/modules/storage.bicep</c>
/// provisions — private endpoint only, shared-key access off, so the app reaches it over its VNet
/// integration with its managed identity (Storage Blob Data Contributor, app-service.bicep). Soft
/// delete and versioning there keep 35 days of anything overwritten or deleted.
///
/// The SHA-256 of each blob's bytes is kept in its metadata: a put of the same bytes is skipped,
/// and a read that doesn't match it is refused.
/// </summary>
public sealed class BlobPdfStore : IPdfStore
{
    private const string HashMetadata = "sha256";

    private readonly BlobServiceClient _service;
    private readonly IReadOnlyDictionary<PdfContainer, string> _containers;

    public BlobPdfStore(BlobServiceClient service, IReadOnlyDictionary<PdfContainer, string> containers)
    {
        _service = service;
        _containers = containers;
    }

    private BlobClient Blob(PdfContainer container, string key) =>
        _service.GetBlobContainerClient(_containers[container]).GetBlobClient(PdfKeys.Checked(key));

    public async Task<PdfPutResult> PutAsync(PdfContainer container, string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
    {
        var blob = Blob(container, key);
        var sha = PdfHash.Sha256Hex(bytes.Span);
        try
        {
            var existing = await InfoAsync(blob, ct);
            if (existing?.Sha256 == sha) return new PdfPutResult(sha, PdfPutOutcome.Unchanged);

            await blob.UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { [HashMetadata] = sha },
            }, ct);
            return new PdfPutResult(sha, existing is null ? PdfPutOutcome.Created : PdfPutOutcome.Replaced);
        }
        catch (Exception e) when (IsStoreFailure(e, ct))
        {
            throw new PdfStoreException($"Could not store {container}/{key}: {Describe(e)}", e);
        }
    }

    public async Task<StoredPdf?> GetAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var blob = Blob(container, key);
        BlobDownloadResult result;
        try
        {
            result = (await blob.DownloadContentAsync(ct)).Value;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
        catch (Exception e) when (IsStoreFailure(e, ct))
        {
            throw new PdfStoreException($"Could not read {container}/{key}: {Describe(e)}", e);
        }

        var bytes = result.Content.ToArray();
        var sha = PdfHash.Sha256Hex(bytes);
        if (result.Details.Metadata.TryGetValue(HashMetadata, out var stored) && !string.Equals(stored, sha, StringComparison.OrdinalIgnoreCase))
            throw new PdfStoreException($"{container}/{key} doesn't match its stored hash.");
        return new StoredPdf(bytes, new StoredPdfInfo(sha, bytes.Length, result.Details.ContentType ?? "application/pdf"));
    }

    public async Task<StoredPdfInfo?> GetInfoAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var blob = Blob(container, key);
        try
        {
            return await InfoAsync(blob, ct);
        }
        catch (Exception e) when (IsStoreFailure(e, ct))
        {
            throw new PdfStoreException($"Could not look up {container}/{key}: {Describe(e)}", e);
        }
    }

    public async Task<bool> DeleteAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var blob = Blob(container, key);
        try
        {
            return (await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct)).Value;
        }
        catch (Exception e) when (IsStoreFailure(e, ct))
        {
            throw new PdfStoreException($"Could not delete {container}/{key}: {Describe(e)}", e);
        }
    }

    private static async Task<StoredPdfInfo?> InfoAsync(BlobClient blob, CancellationToken ct)
    {
        try
        {
            var props = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value;
            props.Metadata.TryGetValue(HashMetadata, out var sha);
            return new StoredPdfInfo(sha?.ToLowerInvariant() ?? "", props.ContentLength, props.ContentType ?? "application/pdf");
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    // Anything the account, the network or the credential throws — but not the caller giving up.
    private static bool IsStoreFailure(Exception e, CancellationToken ct) =>
        e is not PdfStoreException && !(e is OperationCanceledException && ct.IsCancellationRequested);

    private static string Describe(Exception e) => e switch
    {
        RequestFailedException r => $"{r.Status} {r.ErrorCode}",
        _ => e.GetType().Name,
    };
}
