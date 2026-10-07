using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Autorep.Web.Services;

/// <summary>
/// The pulsation analyser PDF a tester attaches to a test (O3) travels inside the test's payload as
/// base64, under <c>pulsationPdf</c>. It is by far the biggest thing in a payload, so neither side
/// keeps it there for long:
/// <list type="bullet">
/// <item>The device keeps the bytes only while it is likely to print them; after that it keeps a
/// pointer (<c>onServer: true</c>, optionally <c>serverTestId</c> — the test whose server copy holds
/// the bytes) and fetches them back when a report needs them.</item>
/// <item>The server never keeps them in <c>PayloadJson</c> at all: on push they go to the PDF store
/// (Services/PulsationAttachments.cs) and the stored payload holds the same pointer shape the device
/// already understands, plus <c>sha256</c> — the hash that names the stored object. A pointer
/// without a <c>sha256</c> names nothing the server holds.</item>
/// </list>
/// These helpers are the server's half of that arrangement. Every one is defensive: a payload that
/// isn't JSON, or has no attachment, comes back unchanged (or null) — the payload is the device's,
/// and the server must never be the thing that breaks it.
/// </summary>
public static class PulsationPayload
{
    private const string Attachment = "pulsationPdf";
    private const string Bytes = "base64";
    private const string Hash = "sha256";
    private const string Holder = "serverTestId";

    // Rewriting a payload must not re-encode it: the default escaper turns every macron in a Māori
    // farm or place name into \u escapes. Same JSON to a parser, but not the text the device sent.
    private static readonly JsonSerializerOptions Write = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static JsonObject? Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            return JsonNode.Parse(payloadJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject? AttachmentOf(JsonObject? payload) => payload?[Attachment] as JsonObject;

    private static string? BytesOf(JsonObject? attachment) =>
        attachment?[Bytes] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    /// <summary>The attachment's base64 bytes, or null when the payload holds none.</summary>
    public static string? Base64(string? payloadJson) => BytesOf(AttachmentOf(Parse(payloadJson)));

    /// <summary>The attachment's file name, or null.</summary>
    public static string? FileName(string? payloadJson) =>
        AttachmentOf(Parse(payloadJson))?["name"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// The payload without the attachment's bytes, marked as held by the server — what a device that
    /// asked for <c>attachments=omit</c> gets on a pull, so a new device's first sync doesn't pull
    /// every analyser PDF the tester ever attached. Unchanged when there are no bytes to take out.
    /// </summary>
    public static string? WithoutBytes(string? payloadJson)
    {
        var payload = Parse(payloadJson);
        var attachment = AttachmentOf(payload);
        if (BytesOf(attachment) is null) return payloadJson;
        attachment!.Remove(Bytes);
        attachment["onServer"] = true;
        return payload!.ToJsonString(Write);
    }

    /// <summary>
    /// True when the payload's attachment says its bytes are held by the server rather than carried
    /// here — a device re-sending a test whose copy it dropped. <paramref name="sourceClientId"/> is the
    /// test whose server copy holds them (null: this same test).
    /// </summary>
    public static bool IsServerPointer(string? payloadJson, out Guid? sourceClientId)
    {
        sourceClientId = null;
        var attachment = AttachmentOf(Parse(payloadJson));
        if (attachment is null || BytesOf(attachment) is not null) return false;
        if (attachment["onServer"] is not JsonValue flag || !flag.TryGetValue<bool>(out var onServer) || !onServer) return false;
        if (attachment["serverTestId"] is JsonValue id && id.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g))
            sourceClientId = g;
        return true;
    }

    /// <summary>
    /// True when both payloads describe the same attachment — same name, size and attach time (the
    /// device's own identity for it: analyser software exports under a fixed file name, so the name
    /// alone isn't enough). Restoring bytes across two DIFFERENT attachments would print one
    /// analyser's results under another's name.
    /// </summary>
    public static bool SameAttachment(string? storedPayloadJson, string? incomingPayloadJson)
    {
        var stored = AttachmentOf(Parse(storedPayloadJson));
        var incoming = AttachmentOf(Parse(incomingPayloadJson));
        if (stored is null || incoming is null) return false;
        return Same(stored["name"], incoming["name"])
            && Same(stored["size"], incoming["size"])
            && Same(stored["attachedAt"], incoming["attachedAt"]);
    }

    private static bool Same(JsonNode? a, JsonNode? b) =>
        a is not null && b is not null && JsonNode.DeepEquals(a, b);

    /// <summary>The payload with the attachment's bytes put back (and the pointer cleared) — what a
    /// device that didn't ask for <c>attachments=omit</c> has always been sent.</summary>
    public static string WithBytes(string payloadJson, string base64)
    {
        var payload = Parse(payloadJson);
        var attachment = AttachmentOf(payload);
        if (attachment is null) return payloadJson;
        attachment[Bytes] = base64;
        attachment.Remove("onServer");
        attachment.Remove(Holder);
        attachment.Remove(Hash);
        return payload!.ToJsonString(Write);
    }

    /// <summary>The hash naming the stored bytes, when the payload's attachment points at the PDF
    /// store (see <see cref="AsStored"/>); null otherwise.</summary>
    public static string? StoredSha256(string? payloadJson) =>
        AttachmentOf(Parse(payloadJson))?[Hash] is JsonValue v && v.TryGetValue<string>(out var s)
        && s.Length == 64 && s.All(char.IsAsciiHexDigitLower)
            ? s
            : null;

    /// <summary>The test whose stored copy holds the bytes (<c>serverTestId</c>), when it isn't the
    /// payload's own test.</summary>
    public static Guid? HolderClientId(string? payloadJson) =>
        AttachmentOf(Parse(payloadJson))?[Holder] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g)
            ? g
            : null;

    /// <summary>
    /// The payload as the server keeps it once the attachment's bytes are in the PDF store: no
    /// bytes, <c>onServer: true</c>, <c>sha256</c> naming the stored object, and <c>serverTestId</c>
    /// only when another of the tester's tests holds it (<paramref name="holderClientId"/>).
    /// Unchanged when there's no attachment.
    /// </summary>
    public static string? AsStored(string? payloadJson, string sha256, Guid? holderClientId)
    {
        var payload = Parse(payloadJson);
        var attachment = AttachmentOf(payload);
        if (attachment is null) return payloadJson;
        attachment.Remove(Bytes);
        attachment["onServer"] = true;
        attachment[Hash] = sha256;
        if (holderClientId is { } holder) attachment[Holder] = holder.ToString();
        else attachment.Remove(Holder);
        return payload!.ToJsonString(Write);
    }

    /// <summary>The payload with any <c>sha256</c> taken off its attachment: a pointer the server
    /// couldn't match to a stored copy must not name one — whatever the device sent.</summary>
    public static string? WithoutStoredHash(string? payloadJson)
    {
        var payload = Parse(payloadJson);
        var attachment = AttachmentOf(payload);
        if (attachment is null || !attachment.ContainsKey(Hash)) return payloadJson;
        attachment.Remove(Hash);
        return payload!.ToJsonString(Write);
    }
}
