using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A pulsator model's OEM spec (legacy <c>Pulsator</c> table, bundled as
/// <c>Client/reference/pulsatorBands.json</c>). Every pulsator a test records must run inside its
/// model's rate and ratio bands. The other figures are held for reference: the device judges phase b,
/// phase d and chamber vacuum against the ISO limits in the test standards. Property names match the
/// bundled JSON, so the device reads a synced spec and a bundled one the same way.
/// </summary>
public sealed record PulsatorSpec
{
    /// <summary>Pulsation rate band, ppm.</summary>
    public double? RateMin { get; init; }
    public double? RateMax { get; init; }
    /// <summary>Pulsation ratio band, %.</summary>
    public double? RatioMin { get; init; }
    public double? RatioMax { get; init; }
    /// <summary>Phase b, %.</summary>
    public double? PhaseB { get; init; }
    /// <summary>Phase d, as the legacy table holds it (15–45, so %, not ms).</summary>
    public double? PhaseD { get; init; }
    /// <summary>As the legacy table holds it (every row is 10). How it relates to 15a is an open
    /// question in plans/reference/standards-audit.md, so nothing judges against it yet.</summary>
    public double? MaxChamberVacuum { get; init; }
    public double? RecommendedRate { get; init; }
    public double? RecommendedRatio { get; init; }
    /// <summary>Free text, as the legacy table holds it ("1", "1 or 2", "2 (2x2)").</summary>
    public string? MaxClustersPerPulsator { get; init; }
}

/// <summary>
/// A vacuum pump's OEM spec (legacy <c>VPModel</c> table, bundled as
/// <c>Client/reference/vacuumPumps.json</c>). The device works out the OEM capacity beside 8a from
/// it: air flow per rpm × the speed 8a was measured at. That figure is a guide and carries no verdict.
/// </summary>
public sealed record VacuumPumpSpec
{
    public double? MinRpm { get; init; }
    public double? MaxRpm { get; init; }
    /// <summary>L/min of air per rpm at 50 kPa.</summary>
    public double? AirFlow { get; init; }
    public double? MotorSizeFactor { get; init; }
    public double? WaterFlowRate { get; init; }
}

/// <summary>Reads and writes <see cref="EquipmentItem.SpecJson"/>.</summary>
public static class OemSpecs
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The equipment types that carry an OEM spec: the two that drive a test's figures.
    /// Releaser sizing is keyed by clusters × heads, not by model, so it isn't one of them.</summary>
    public static readonly string[] SpecTypes = [EquipmentItem.Pulsator, EquipmentItem.VacuumPump];

    public static bool HasSpec(string type) => SpecTypes.Contains(type);

    public static PulsatorSpec? Pulsator(EquipmentItem item) =>
        item.Type == EquipmentItem.Pulsator ? Parse<PulsatorSpec>(item.SpecJson) : null;

    public static VacuumPumpSpec? VacuumPump(EquipmentItem item) =>
        item.Type == EquipmentItem.VacuumPump ? Parse<VacuumPumpSpec>(item.SpecJson) : null;

    /// <summary>The item's spec as its typed record, or null when it has none.</summary>
    public static object? For(EquipmentItem item) => item.Type switch
    {
        EquipmentItem.Pulsator => Pulsator(item),
        EquipmentItem.VacuumPump => VacuumPump(item),
        _ => null,
    };

    /// <summary>The JSON to store for a spec; null for no spec, or a spec with nothing entered.</summary>
    public static string? Serialize(object? spec)
    {
        if (spec is null) return null;
        var json = JsonSerializer.Serialize(spec, spec.GetType(), Json);
        return json == "{}" ? null : json;
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
