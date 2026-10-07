using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Autorep.Web.Services;

/// <summary>
/// The pulsation analyser PDF a tester attaches to a test (O3) travels inside the test's
/// <c>PayloadJson</c> as base64, under <c>pulsationPdf</c>. It is by far the biggest thing in a
/// payload, so the device keeps it only while it is likely to print it: after a test is complete and
/// safely on the server, the device drops its copy and keeps a pointer (<c>onServer: true</c>,
/// optionally <c>serverTestId</c> — the test whose server copy holds the bytes), fetching the bytes
/// back when a report needs them. These helpers are the server's half of that arrangement.
///
/// Every helper is defensive: a payload that isn't JSON, or has no attachment, comes back unchanged
/// (or null) — the payload is the device's, and the server must never be the thing that breaks it.
/// </summary>
public static class PulsationPayload
{
    private const string Attachment = "pulsationPdf";
    private const string Bytes = "base64";

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

    /// <summary>The payload with the attachment's bytes put back (and the pointer cleared).</summary>
    public static string WithBytes(string payloadJson, string base64)
    {
        var payload = Parse(payloadJson);
        var attachment = AttachmentOf(payload);
        if (attachment is null) return payloadJson;
        attachment[Bytes] = base64;
        attachment.Remove("onServer");
        attachment.Remove("serverTestId");
        return payload!.ToJsonString(Write);
    }
}
