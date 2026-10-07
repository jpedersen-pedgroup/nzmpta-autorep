using System.Text.Json;

namespace Autorep.Web.Services.Pdfs;

/// <summary>
/// The store in Development: a folder (gitignored <c>App_Data/pdf-store</c> by default), one
/// sub-folder per container, the key as the path beneath it. Each PDF has a <c>.meta.json</c>
/// beside it holding its hash and content type, which play the part of a blob's metadata.
/// Not for any shared environment: an App Service's local disk doesn't survive a redeploy.
/// </summary>
public sealed class LocalFolderPdfStore : IPdfStore
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<PdfContainer, string> _containers;

    private sealed record Meta(string Sha256, string ContentType);

    public LocalFolderPdfStore(string root, IReadOnlyDictionary<PdfContainer, string> containers)
    {
        _root = Path.GetFullPath(root);
        _containers = containers;
    }

    private string PathFor(PdfContainer container, string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, _containers[container], PdfKeys.Checked(key).Replace('/', Path.DirectorySeparatorChar)));
        // PdfKeys already refuses anything that could climb out; this is the belt to its braces.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"Key escapes the store: '{key}'.", nameof(key));
        return path;
    }

    private static string MetaPath(string path) => path + ".meta.json";

    public async Task<PdfPutResult> PutAsync(PdfContainer container, string key, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
    {
        var path = PathFor(container, key);
        var sha = PdfHash.Sha256Hex(bytes.Span);
        var existing = await ReadMetaAsync(path, ct);
        if (existing?.Sha256 == sha && File.Exists(path)) return new PdfPutResult(sha, PdfPutOutcome.Unchanged);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write beside, then move into place: a reader never sees half a file.
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes.ToArray(), ct);
            File.Move(temp, path, overwrite: true);
            await File.WriteAllTextAsync(MetaPath(path), JsonSerializer.Serialize(new Meta(sha, contentType)), ct);
        }
        catch (IOException e)
        {
            throw new PdfStoreException($"Could not write {container}/{key}.", e);
        }
        return new PdfPutResult(sha, existing is null ? PdfPutOutcome.Created : PdfPutOutcome.Replaced);
    }

    public async Task<StoredPdf?> GetAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var path = PathFor(container, key);
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var meta = await ReadMetaAsync(path, ct);
        var sha = PdfHash.Sha256Hex(bytes);
        if (meta is not null && meta.Sha256 != sha)
            throw new PdfStoreException($"{container}/{key} doesn't match its stored hash.");
        return new StoredPdf(bytes, new StoredPdfInfo(sha, bytes.Length, meta?.ContentType ?? "application/pdf"));
    }

    public async Task<StoredPdfInfo?> GetInfoAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var path = PathFor(container, key);
        if (!File.Exists(path)) return null;
        var meta = await ReadMetaAsync(path, ct);
        return new StoredPdfInfo(meta?.Sha256 ?? "", new FileInfo(path).Length, meta?.ContentType ?? "application/pdf");
    }

    public Task<bool> DeleteAsync(PdfContainer container, string key, CancellationToken ct = default)
    {
        var path = PathFor(container, key);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        File.Delete(MetaPath(path));
        return Task.FromResult(true);
    }

    private static async Task<Meta?> ReadMetaAsync(string path, CancellationToken ct)
    {
        var metaPath = MetaPath(path);
        if (!File.Exists(metaPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<Meta>(await File.ReadAllTextAsync(metaPath, ct));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
