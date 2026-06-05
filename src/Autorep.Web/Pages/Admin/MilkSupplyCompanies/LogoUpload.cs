using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Pages.Admin.MilkSupplyCompanies;

internal static class LogoUpload
{
    public const long MaxBytes = 1_000_000; // 1 MB

    // Reads an uploaded image into the company's logo fields. Returns false (and adds an
    // error) if a file is present but invalid; true otherwise (including "no file").
    public static async Task<bool> ApplyAsync(IFormFile? file, MilkSupplyCompany company, List<string> errors)
    {
        if (file is null || file.Length == 0) return true;
        if (file.Length > MaxBytes) { errors.Add("Logo must be under 1 MB."); return false; }
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Logo must be an image file.");
            return false;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        company.LogoData = ms.ToArray();
        company.LogoContentType = file.ContentType;
        return true;
    }
}
