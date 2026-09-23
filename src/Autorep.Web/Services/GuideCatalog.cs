using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace Autorep.Web.Services;

/// <summary>One work-instruction PDF as listed on the Help &amp; guides page.</summary>
/// <param name="Hash">Short content hash of the PDF: its ETag, and the <c>?v=</c> on its links.</param>
public sealed record Guide(
    string Id,
    string Title,
    string Description,
    string Version,
    DateOnly Date,
    string File,
    IReadOnlyList<string> Roles,
    long SizeBytes,
    string Hash)
{
    /// <summary>Versioned link. The service worker keys its offline copy on the path alone, so the
    /// query string only defeats browser caches — it never strands a device on an old copy.</summary>
    public string Url => $"/guides/{File}?v={Hash}";

    public bool IsVisibleTo(ClaimsPrincipal user) => Roles.Any(user.IsInRole);
}

/// <summary>
/// The work-instruction PDFs under <c>Guides/</c>, described by <c>Guides/guides.json</c> — the
/// one place guide metadata lives (the client bundle imports the same file). Read once, lazily,
/// on first use: sizes and hashes come from the files themselves so publishing a new version is
/// "replace the PDF, bump the version".
///
/// The PDFs live outside wwwroot on purpose: UseStaticFiles runs before authentication and would
/// hand the admin guides to anyone. They are served by <see cref="Api.GuidesController"/> instead.
/// </summary>
public class GuideCatalog
{
    public const string Folder = "Guides";

    private readonly string _root;
    private readonly Lazy<IReadOnlyList<Guide>> _guides;

    public GuideCatalog(IWebHostEnvironment env, ILogger<GuideCatalog> log)
    {
        _root = Path.Combine(env.ContentRootPath, Folder);
        _guides = new Lazy<IReadOnlyList<Guide>>(() => Load(_root, log));
    }

    public IReadOnlyList<Guide> All => _guides.Value;

    public IReadOnlyList<Guide> VisibleTo(ClaimsPrincipal user) =>
        All.Where(g => g.IsVisibleTo(user)).ToList();

    /// <summary>Exact file-name match against the catalogue — never a path built from the request.</summary>
    public Guide? Find(string file) =>
        All.FirstOrDefault(g => string.Equals(g.File, file, StringComparison.OrdinalIgnoreCase));

    public string PhysicalPath(Guide guide) => Path.Combine(_root, guide.File);

    private sealed record Entry(
        string Id, string Title, string Description, string Version, DateOnly Date, string File, string[] Roles);

    private sealed record Manifest(Entry[] Guides);

    private static IReadOnlyList<Guide> Load(string root, ILogger log)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(
            File.ReadAllText(Path.Combine(root, "guides.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Guides/guides.json is empty.");

        var guides = new List<Guide>();
        foreach (var e in manifest.Guides)
        {
            var path = Path.Combine(root, e.File);
            if (!System.IO.File.Exists(path))
            {
                // A half-finished publish shouldn't take the other guides down with it — but it must
                // be loud. GuideCatalogTests fails the build on the same condition.
                log.LogError("Guide {GuideId} lists {File}, which isn't in {Folder}/ — hiding it.", e.Id, e.File, Folder);
                continue;
            }
            using var stream = System.IO.File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
            guides.Add(new Guide(e.Id, e.Title, e.Description, e.Version, e.Date, e.File, e.Roles,
                new FileInfo(path).Length, hash));
        }
        return guides;
    }
}
