using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Autorep.Web.Services.Pdfs;

/// <summary>
/// Where each PDF lives in the store. Deterministic, so a retry lands on the same object, and
/// always under the owning tester's prefix:
/// <list type="bullet">
/// <item><c>{testerId}/{clientId}/final-report.pdf</c> — the Final Report as signed off. One per
/// test row, so each version of a test has its own.</item>
/// <item><c>{testerId}/{clientId}/pulsation/{sha256}.pdf</c> — an attached analyser PDF, under the
/// test that first sent it and named by its hash: the same bytes sent again are the same object.</item>
/// </list>
/// </summary>
public static partial class PdfKeys
{
    public static string FinalReport(string testerId, Guid clientId) =>
        $"{Segment(testerId)}/{clientId:D}/final-report.pdf";

    public static string Pulsation(string testerId, Guid holderClientId, string sha256) =>
        $"{Segment(testerId)}/{holderClientId:D}/pulsation/{CheckedHash(sha256)}.pdf";

    /// <summary>True for a key this class could have made: segments of letters, digits, '.', '_'
    /// and '-', no empty or dot-only segment. The stores refuse anything else, so a key can never
    /// climb out of its container (the folder store maps it onto a path).</summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= 512
        && key.Split('/').All(s => SafeSegment().IsMatch(s) && s.Trim('.').Length > 0);

    /// <summary>The key, or an exception when it isn't one.</summary>
    public static string Checked(string key) =>
        IsValid(key) ? key : throw new ArgumentException($"Not a PDF store key: '{key}'.", nameof(key));

    // A tester id is an Identity user id (a GUID string), but it reaches here from a claim or a
    // database row, so check it rather than trust it.
    private static string Segment(string id) =>
        SafeSegment().IsMatch(id) && id.Trim('.').Length > 0
            ? id
            : throw new ArgumentException($"Not usable in a PDF store key: '{id}'.", nameof(id));

    private static string CheckedHash(string sha256) =>
        Sha256Hex().IsMatch(sha256) ? sha256 : throw new ArgumentException($"Not a SHA-256: '{sha256}'.", nameof(sha256));

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex SafeSegment();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}

public static class PdfHash
{
    /// <summary>SHA-256 of the bytes, lowercase hex — the form every store and record keeps.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Whether the bytes start like a PDF file ("%PDF-"). A cheap guard against storing
    /// an HTML error page or a truncated upload as a report, not a validation of the document.</summary>
    public static bool LooksLikePdf(ReadOnlySpan<byte> bytes) => bytes.StartsWith("%PDF-"u8);
}
