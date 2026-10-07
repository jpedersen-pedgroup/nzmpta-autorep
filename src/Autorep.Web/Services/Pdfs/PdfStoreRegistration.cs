using Azure.Identity;
using Azure.Storage.Blobs;

namespace Autorep.Web.Services.Pdfs;

public enum PdfStoreProvider
{
    Blob,
    LocalFolder,
    InMemory,
}

/// <summary>
/// Picks the PDF store for the environment:
/// <list type="bullet">
/// <item>Testing (xUnit, Playwright) — in memory, so no test needs Azure.</item>
/// <item>Development — a gitignored folder (<c>PdfStore:LocalPath</c>, default
/// <c>App_Data/pdf-store</c> under the content root).</item>
/// <item>Everything else (Staging, Production) — Azure Blob Storage, in the account named by
/// <c>AzureStorage:AccountName</c> (set by infra/modules/app-service.bicep), reached with the App
/// Service's managed identity. A missing account name stops the app at startup: quietly keeping
/// tester reports on an App Service's local disk would lose them at the next deploy.</item>
/// </list>
/// <c>PdfStore:Provider</c> (Blob, LocalFolder, InMemory) overrides the choice anywhere except
/// Production, which always uses Blob.
/// </summary>
public static class PdfStoreRegistration
{
    public static PdfStoreProvider Choose(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["PdfStore:Provider"];
        PdfStoreProvider provider;
        if (env.IsProduction())
        {
            provider = PdfStoreProvider.Blob;
        }
        else if (!string.IsNullOrWhiteSpace(configured))
        {
            provider = Enum.TryParse<PdfStoreProvider>(configured, ignoreCase: true, out var p)
                ? p
                : throw new InvalidOperationException($"PdfStore:Provider '{configured}' is not one of Blob, LocalFolder, InMemory.");
        }
        else if (env.IsEnvironment("Testing"))
        {
            provider = PdfStoreProvider.InMemory;
        }
        else
        {
            provider = env.IsDevelopment() ? PdfStoreProvider.LocalFolder : PdfStoreProvider.Blob;
        }

        if (provider == PdfStoreProvider.Blob && string.IsNullOrWhiteSpace(config["AzureStorage:AccountName"]))
            throw new InvalidOperationException(
                $"AzureStorage:AccountName must be set in {env.EnvironmentName}: Final Reports and pulsation PDFs are kept in Azure Blob Storage there.");
        return provider;
    }

    public static IReadOnlyDictionary<PdfContainer, string> Containers(IConfiguration config) => new Dictionary<PdfContainer, string>
    {
        [PdfContainer.FinalReports] = Named(config["AzureStorage:FinalReportsContainer"], "final-reports"),
        [PdfContainer.PulsationData] = Named(config["AzureStorage:PulsationDataContainer"], "pulsation-data"),
    };

    private static string Named(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    public static IServiceCollection AddPdfStore(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var containers = Containers(config);
        switch (Choose(config, env))
        {
            case PdfStoreProvider.Blob:
                var account = config["AzureStorage:AccountName"]!.Trim();
                // ManagedIdentityCredential rather than DefaultAzureCredential, as for Key Vault in
                // Program.cs: one fast, predictable token path inside App Service.
                services.AddSingleton<IPdfStore>(_ => new BlobPdfStore(
                    new BlobServiceClient(new Uri($"https://{account}.blob.core.windows.net"), new ManagedIdentityCredential()),
                    containers));
                break;
            case PdfStoreProvider.LocalFolder:
                var path = config["PdfStore:LocalPath"];
                var root = Path.Combine(env.ContentRootPath, string.IsNullOrWhiteSpace(path) ? Path.Combine("App_Data", "pdf-store") : path);
                services.AddSingleton<IPdfStore>(new LocalFolderPdfStore(root, containers));
                break;
            case PdfStoreProvider.InMemory:
                services.AddSingleton<InMemoryPdfStore>();
                services.AddSingleton<IPdfStore>(sp => sp.GetRequiredService<InMemoryPdfStore>());
                break;
        }
        return services;
    }
}
