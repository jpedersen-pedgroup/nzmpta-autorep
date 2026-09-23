using System.Security.Cryptography;
using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Services;

/// <summary>
/// The Testing Company's report logo — uploaded from the admin portal, printed in the Test Summary
/// header. The report is built on-device by pdfmake, which only embeds PNG and JPEG reliably, so
/// those are the only formats accepted. The type is decided from the file's own bytes, never from
/// its extension or the browser-supplied Content-Type (both are whatever the uploader says).
/// </summary>
public static class CompanyLogo
{
    public const long MaxBytes = 1_000_000; // 1 MB

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>"image/png" / "image/jpeg" from the magic bytes; null for anything else.</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature)) return "image/png";
        // JPEG: SOI marker (FF D8) followed by the first segment's marker byte (FF).
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        return null;
    }

    /// <summary>Whether a stored logo will print on the report. Logos migrated from the legacy
    /// system predate the PNG/JPEG rule, so the admin page flags any that won't.</summary>
    public static bool IsPrintable(byte[]? data) => data is { Length: > 0 } && SniffContentType(data) is not null;

    /// <summary>Strong validator for the served logo: a content hash, so it changes exactly when the
    /// bytes do and the device knows to re-download on its next sync. No schema column needed.</summary>
    public static string ETag(byte[] data) =>
        "\"" + Convert.ToHexString(SHA256.HashData(data), 0, 8).ToLowerInvariant() + "\"";

    // Reads an uploaded logo into the company. Returns false (and adds an error) if a file is present
    // but invalid; true otherwise (including "no file", which leaves the current logo alone).
    public static async Task<bool> ApplyAsync(IFormFile? file, TestingCompany company, List<string> errors)
    {
        if (file is null || file.Length == 0) return true;
        if (file.Length > MaxBytes)
        {
            // Rounded UP to a tenth, so a file a few bytes over never reads as "that file is 1 MB".
            var mb = Math.Ceiling(file.Length / 100_000.0) / 10;
            errors.Add($"Logo must be 1 MB or smaller — that file is {mb:0.0} MB. " +
                       "Try exporting it at a smaller size.");
            return false;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var contentType = SniffContentType(bytes);
        if (contentType is null)
        {
            errors.Add("Logo must be a PNG or JPEG image (SVG, WebP and GIF can't be printed on the report).");
            return false;
        }

        company.LogoData = bytes;
        company.LogoContentType = contentType;
        return true;
    }
}
