using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Autorep.Web.Services;

/// <summary>
/// A test's payload (the device's serialised LocalTest) as the separately editable fields a person
/// can change — "units". Each configuration property, each reading, each visual-check item, each
/// recommendation and each recorded measurement is a unit of its own; a list edited as rows (the
/// pulsator and cluster tables, the pump lists) is one unit; any other top-level field is one unit.
/// Client/versioning/merge.ts divides a payload the same way, so the server's view of "what changed"
/// and "what both sides changed" agrees with the device's.
///
/// Used to hold an administrator's edit to what their role may change (compared against the version
/// it replaces, never trusted from the client), to record which fields an edit touched, and to work
/// out which fields two colliding versions both changed.
/// </summary>
public static class PayloadUnits
{
    /// <summary>The device's and the server's own bookkeeping: never compared as test data. The
    /// server writes these itself on every version it creates.</summary>
    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal)
    {
        "id", "version", "supersedesId", "mergedFromId", "syncState", "everUploaded", "readonly",
        "currentStep", "createdAt", "updatedAt", "markedCompleteAt", "amendments", "attestations",
        "deletedOnServer",
    };

    /// <summary>Fields held as a map of separately edited entries.</summary>
    private static readonly HashSet<string> Keyed = new(StringComparer.Ordinal)
    {
        "config", "readings", "visualFaults", "recommendations", "dataFields",
    };

    /// <summary>The same in every version of a test, whoever makes it: who did the test, for whom,
    /// at which farm, and a migrated test's as-recorded fields.</summary>
    public static readonly IReadOnlySet<string> Fixed = new HashSet<string>(StringComparer.Ordinal)
    {
        "farmId", "farmName", "farm", "testedBy", "testingCompanyId", "testingCompanyName",
        "verdicts", "recordedRecommendations", "recordedVisualFaults", "legacy",
    };

    public static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A migrated legacy test: raw legacy columns rather than a LocalTest (the same test the
    /// client's legacy adapter makes).</summary>
    public static bool IsLegacy(JsonObject? payload) =>
        payload is not null && payload.ContainsKey("legacy") && !payload.ContainsKey("currentStep");

    /// <summary>The units whose values differ between two payloads, in a stable order.</summary>
    public static SortedSet<string> Changed(JsonObject? before, JsonObject? after)
    {
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in KeysOf(before).Union(KeysOf(after)))
        {
            if (Bookkeeping.Contains(key)) continue;
            if (Keyed.Contains(key))
            {
                var b = before?[key] as JsonObject;
                var a = after?[key] as JsonObject;
                foreach (var inner in KeysOf(b).Union(KeysOf(a)))
                    if (!Same(b?[inner], a?[inner])) changed.Add($"{key}.{inner}");
                continue;
            }
            if (!Same(Comparable(key, before?[key]), Comparable(key, after?[key]))) changed.Add(key);
        }
        return changed;
    }

    /// <summary>
    /// The units two versions made from the same base both changed — to different values. The same
    /// change made on both sides isn't a collision. Calculated readings are left out: they follow their
    /// inputs (and the merge recomputes them), so listing them would only repeat an input that is
    /// already listed.
    /// </summary>
    public static SortedSet<string> Overlapping(JsonObject? @base, JsonObject? head, JsonObject? incoming)
    {
        var both = Changed(@base, head);
        both.IntersectWith(Changed(@base, incoming));
        both.RemoveWhere(unit => unit.StartsWith("readings.", StringComparison.Ordinal)
            && DerivedReadings.IsDerived(unit["readings.".Length..]));
        both.RemoveWhere(unit => Same(ValueAt(head, unit), ValueAt(incoming, unit)));
        return both;
    }

    /// <summary>
    /// Where a device's combine of two versions doesn't follow the reconciliation rule, field by
    /// field: <paramref name="incoming"/>'s value wherever it changed something (alone, or as the
    /// later arrival where both changed it), <paramref name="head"/>'s everywhere else, and the head's
    /// for the fields no version changes. Calculated readings are skipped — the device recomputes them
    /// from the merged inputs. Empty for a faithful combine; anything listed would mean the device
    /// dropped or invented a change, and the combine is refused.
    /// </summary>
    public static IReadOnlyList<string> MergeDepartures(
        JsonObject? @base, JsonObject? head, JsonObject? incoming, JsonObject? merged)
    {
        var departures = new List<string>();
        foreach (var unit in new[] { @base, head, incoming, merged }.SelectMany(UnitsOf).Distinct())
        {
            var dot = unit.IndexOf('.');
            var top = dot > 0 && Keyed.Contains(unit[..dot]) ? unit[..dot] : unit;
            if (top == "readings" && DerivedReadings.IsDerived(unit["readings.".Length..])) continue;
            var expected = Fixed.Contains(top) || Same(ValueAt(incoming, unit), ValueAt(@base, unit))
                ? ValueAt(head, unit)
                : ValueAt(incoming, unit);
            if (!Same(ValueAt(merged, unit), expected)) departures.Add(unit);
        }
        departures.Sort(StringComparer.Ordinal);
        return departures;
    }

    /// <summary>The units a payload has a value for (the same division as <see cref="Changed"/>).</summary>
    private static IEnumerable<string> UnitsOf(JsonObject? payload)
    {
        foreach (var key in KeysOf(payload))
        {
            if (Bookkeeping.Contains(key)) continue;
            if (Keyed.Contains(key))
            {
                foreach (var inner in KeysOf(payload![key] as JsonObject)) yield return $"{key}.{inner}";
                continue;
            }
            yield return key;
        }
    }

    private static JsonNode? ValueAt(JsonObject? payload, string unit)
    {
        var dot = unit.IndexOf('.');
        if (dot > 0 && Keyed.Contains(unit[..dot]))
            return (payload?[unit[..dot]] as JsonObject)?[unit[(dot + 1)..]];
        return Comparable(unit, payload?[unit]);
    }

    private static IEnumerable<string> KeysOf(JsonObject? o) =>
        o is null ? [] : o.Select(p => p.Key);

    /// <summary>The analyser PDF is the same attachment whether a copy carries its bytes or only a
    /// pointer to the server's copy: it's identified by name, size and attach time (as
    /// PulsationPayload.SameAttachment does).</summary>
    private static JsonNode? Comparable(string key, JsonNode? value)
    {
        if (key != "pulsationPdf" || value is not JsonObject pdf) return value;
        return new JsonObject
        {
            ["name"] = pdf["name"]?.DeepClone(),
            ["size"] = pdf["size"]?.DeepClone(),
            ["attachedAt"] = pdf["attachedAt"]?.DeepClone(),
        };
    }

    /// <summary>
    /// Value equality the way the device means it: an absent field and a null one are the same, an
    /// object's null-valued members are as good as missing, and numbers compare by value (60 and
    /// 60.0 are one reading).
    /// </summary>
    public static bool Same(JsonNode? a, JsonNode? b)
    {
        if (IsNull(a) || IsNull(b)) return IsNull(a) && IsNull(b);
        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
                foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                    if (!Same(oa[key], ob[key])) return false;
                return true;
            case JsonArray aa when b is JsonArray ab:
                if (aa.Count != ab.Count) return false;
                for (var i = 0; i < aa.Count; i++)
                    if (!Same(aa[i], ab[i])) return false;
                return true;
            case JsonValue va when b is JsonValue vb:
                return SameValue(va, vb);
            default:
                return false;
        }
    }

    private static bool IsNull(JsonNode? n) =>
        n is null || (n is JsonValue v && v.GetValueKind() == JsonValueKind.Null);

    private static bool SameValue(JsonValue a, JsonValue b)
    {
        var ka = a.GetValueKind();
        var kb = b.GetValueKind();
        if (ka == JsonValueKind.Number && kb == JsonValueKind.Number)
            return ToDecimal(a) is { } da && ToDecimal(b) is { } db ? da == db : a.ToJsonString() == b.ToJsonString();
        if (ka != kb) return false;
        return ka switch
        {
            JsonValueKind.String => a.GetValue<string>() == b.GetValue<string>(),
            JsonValueKind.True or JsonValueKind.False => true,
            _ => a.ToJsonString() == b.ToJsonString(),
        };
    }

    private static decimal? ToDecimal(JsonValue v) =>
        decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>
/// The readings the wizard calculates from other readings (Client/passfail/derived.ts) rather than
/// the tester typing them. Only the keys are needed here; a parity test reads derived.ts so the two
/// lists can't drift apart.
/// </summary>
public static class DerivedReadings
{
    public const string OemCapacityPrefix = "tr.pumpOemCapacity";

    public static readonly IReadOnlySet<string> Keys = new HashSet<string>(StringComparer.Ordinal)
    {
        "tr.regulationDeviation", "tr.regulationLoss", "tr.regulatorLeakage", "tr.requiredEffectiveReserve",
        "tr.requiredCleaningReserve", "tr.fallOff", "tr.regulationUndershoot", "tr.regulationOvershoot",
        "tr.airlineDropRR", "tr.airlinePumpDrop", "tr.regulatorSensitivity", "tr.gaugeError1",
        "tr.gaugeError2", "tr.gaugeError3", "add.vacuumSystemLeakage", "add.milkSystemLeakage",
        "add.acrConsumption", "add.clusterAirAdmission", "puls.milkSystemAncillary",
        "puls.pulsatorConsumption", "puls.vacuumSystemAncillary", "puls.testPulsationReading",
        "puls.rateSpread", "puls.ratioSpread",
    };

    public static bool IsDerived(string key) =>
        Keys.Contains(key) || key.StartsWith(OemCapacityPrefix, StringComparison.Ordinal);
}
