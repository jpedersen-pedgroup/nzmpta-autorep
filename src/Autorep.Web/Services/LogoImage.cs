using System.Text;

namespace Autorep.Web.Services;

// Company logos print on the report, which is generated on-device by pdfmake. pdfmake draws PNG
// and JPEG as images and SVG as vectors, and throws on anything else, which would take the whole
// report down with it. So a logo is accepted on what its bytes are, never on the file name or the
// type the browser labelled it with, and it is stored under the type it actually is.
public static class LogoImage
{
    public const long MaxBytes = 1_000_000; // 1 MB

    /// <summary>The file-picker filter for a logo input.</summary>
    public const string Accept = "image/png,image/jpeg,image/svg+xml,.png,.jpg,.jpeg,.svg";

    public const string FormatError = "Logo must be a PNG, JPEG or SVG image.";

    public sealed record Logo(byte[] Data, string ContentType);

    /// <summary>"image/png", "image/jpeg" or "image/svg+xml" from the file's own bytes, or null for
    /// anything else (GIF, WebP, a renamed PDF, a PNG header on garbage too short to be one).</summary>
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.StartsWith(png)) return "image/png";
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        return IsSvg(data) ? "image/svg+xml" : null;
    }

    // An SVG is XML whose root element is <svg>: skip a BOM, whitespace, the XML declaration,
    // comments and a DOCTYPE, and the next thing must be the svg start tag. Only the head of the
    // file is read; a root element buried after 4 KB of comments isn't a logo worth supporting.
    private static bool IsSvg(ReadOnlySpan<byte> data)
    {
        var head = data[..Math.Min(data.Length, 4096)];
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) head = head[3..];
        // Lenient decode: the 4 KB cut can split a character, and binary data simply won't read as
        // markup below.
        var s = Encoding.UTF8.GetString(head).AsSpan();
        while (true)
        {
            s = s.TrimStart();
            if (s.StartsWith("<?")) s = After(s, "?>");
            else if (s.StartsWith("<!--")) s = After(s, "-->");
            else if (s.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) s = After(s, ">");
            else break;
        }
        return s.StartsWith("<svg") && s.Length > 4 && (char.IsWhiteSpace(s[4]) || s[4] is '>' or '/');
    }

    private static ReadOnlySpan<char> After(ReadOnlySpan<char> s, string end)
    {
        var i = s.IndexOf(end);
        return i < 0 ? ReadOnlySpan<char>.Empty : s[(i + end.Length)..];
    }

    /// <summary>A stored logo as a data URL (for the on-device report generator and admin previews),
    /// or null when there is none.</summary>
    public static string? DataUrl(byte[]? data, string? contentType) =>
        data is { Length: > 0 }
            ? $"data:{contentType ?? "application/octet-stream"};base64,{Convert.ToBase64String(data)}"
            : null;

    /// <summary>Reads an admin's logo upload. <c>Ok</c> is false (with an error added) when a file
    /// was chosen but isn't acceptable; true with a null <c>Logo</c> when no file was chosen.</summary>
    public static async Task<(bool Ok, Logo? Logo)> ReadUploadAsync(IFormFile? file, List<string> errors)
    {
        if (file is null || file.Length == 0) return (true, null);
        if (file.Length > MaxBytes)
        {
            errors.Add("Logo must be under 1 MB.");
            return (false, null);
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();
        var type = Sniff(data);
        if (type is null)
        {
            errors.Add(FormatError);
            return (false, null);
        }
        return (true, new Logo(data, type));
    }
}
