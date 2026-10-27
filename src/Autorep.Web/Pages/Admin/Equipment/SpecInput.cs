using System.Globalization;
using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Pages.Admin.Equipment;

/// <summary>
/// The OEM spec fields on the New/Edit pages (shared partial _SpecFields), for the types that carry
/// a spec (<see cref="OemSpecs.HasSpec"/>). Every figure is optional: a model with no rate band
/// simply gets no rate verdict on the device. What is entered must make sense, though, because the
/// device judges every pulsator a test records against it.
/// </summary>
public class SpecInput
{
    // Pulsator
    public double? RateMin { get; set; }
    public double? RateMax { get; set; }
    public double? RatioMin { get; set; }
    public double? RatioMax { get; set; }
    public double? PhaseB { get; set; }
    public double? PhaseD { get; set; }
    public double? MaxChamberVacuum { get; set; }
    public double? RecommendedRate { get; set; }
    public double? RecommendedRatio { get; set; }
    public string? MaxClustersPerPulsator { get; set; }

    // Vacuum pump
    public double? MinRpm { get; set; }
    public double? MaxRpm { get; set; }
    public double? AirFlow { get; set; }
    public double? MotorSizeFactor { get; set; }
    public double? WaterFlowRate { get; set; }

    public static SpecInput From(EquipmentItem item)
    {
        var p = OemSpecs.Pulsator(item);
        var v = OemSpecs.VacuumPump(item);
        return new SpecInput
        {
            RateMin = p?.RateMin, RateMax = p?.RateMax, RatioMin = p?.RatioMin, RatioMax = p?.RatioMax,
            PhaseB = p?.PhaseB, PhaseD = p?.PhaseD, MaxChamberVacuum = p?.MaxChamberVacuum,
            RecommendedRate = p?.RecommendedRate, RecommendedRatio = p?.RecommendedRatio,
            MaxClustersPerPulsator = p?.MaxClustersPerPulsator,
            MinRpm = v?.MinRpm, MaxRpm = v?.MaxRpm, AirFlow = v?.AirFlow,
            MotorSizeFactor = v?.MotorSizeFactor, WaterFlowRate = v?.WaterFlowRate,
        };
    }

    /// <summary>The <see cref="EquipmentItem.SpecJson"/> for an item of <paramref name="type"/>: null
    /// for a type without specs, or when nothing is entered.</summary>
    public string? ToSpecJson(string type) => type switch
    {
        EquipmentItem.Pulsator => OemSpecs.Serialize(new PulsatorSpec
        {
            RateMin = RateMin, RateMax = RateMax, RatioMin = RatioMin, RatioMax = RatioMax,
            PhaseB = PhaseB, PhaseD = PhaseD, MaxChamberVacuum = MaxChamberVacuum,
            RecommendedRate = RecommendedRate, RecommendedRatio = RecommendedRatio,
            MaxClustersPerPulsator = string.IsNullOrWhiteSpace(MaxClustersPerPulsator) ? null : MaxClustersPerPulsator.Trim(),
        }),
        EquipmentItem.VacuumPump => OemSpecs.Serialize(new VacuumPumpSpec
        {
            MinRpm = MinRpm, MaxRpm = MaxRpm, AirFlow = AirFlow,
            MotorSizeFactor = MotorSizeFactor, WaterFlowRate = WaterFlowRate,
        }),
        _ => null,
    };

    /// <summary>Adds a message per problem; true when there are none. Only the fields of
    /// <paramref name="type"/> are checked — the others aren't saved.</summary>
    public bool Validate(string type, ICollection<string> errors)
    {
        var before = errors.Count;
        if (type == EquipmentItem.Pulsator)
        {
            Band("Rate", RateMin, RateMax, 300, " ppm", errors);
            Band("Ratio", RatioMin, RatioMax, 100, "%", errors);
            Within("Phase b", PhaseB, 0, 100, "%", errors);
            Within("Phase d", PhaseD, 0, 100, "%", errors);
            Within("Max chamber vacuum", MaxChamberVacuum, 0, 100, null, errors);
            Positive("Recommended rate", RecommendedRate, 300, " ppm", errors);
            Positive("Recommended ratio", RecommendedRatio, 100, "%", errors);
            if (MaxClustersPerPulsator?.Trim().Length > 50)
                errors.Add("Max clusters per pulsator must be 50 characters or fewer.");
        }
        else if (type == EquipmentItem.VacuumPump)
        {
            if (MinRpm.HasValue != MaxRpm.HasValue)
                errors.Add("Speed range: enter both the minimum and the maximum rpm, or neither.");
            Within("Minimum speed", MinRpm, 0, 10000, " rpm", errors);
            Within("Maximum speed", MaxRpm, 0, 10000, " rpm", errors);
            if (MinRpm > MaxRpm) errors.Add("Speed range: the minimum rpm can't be above the maximum.");
            Positive("Air flow per rpm", AirFlow, 50, " L/min", errors);
            Positive("Motor size factor", MotorSizeFactor, 1000, null, errors);
            Within("Water flow rate", WaterFlowRate, 0, 1000, null, errors);
        }
        return errors.Count == before;
    }

    // A pulsator band is a pair: the device needs both ends to judge against it.
    private static void Band(string label, double? min, double? max, double ceiling, string unit, ICollection<string> errors)
    {
        if (min.HasValue != max.HasValue)
        {
            errors.Add($"{label} band: enter both the minimum and the maximum, or neither.");
            return;
        }
        if (min is null || max is null) return;
        if (min <= 0 || max > ceiling)
            errors.Add($"{label} band must be above 0 and at most {N(ceiling)}{unit}.");
        else if (min > max)
            errors.Add($"{label} band: the minimum can't be above the maximum.");
    }

    private static void Within(string label, double? value, double min, double max, string? unit, ICollection<string> errors)
    {
        if (value is null || (value >= min && value <= max)) return;
        errors.Add($"{label} must be between {N(min)} and {N(max)}{unit}.");
    }

    private static void Positive(string label, double? value, double max, string? unit, ICollection<string> errors)
    {
        if (value is null || (value > 0 && value <= max)) return;
        errors.Add($"{label} must be above 0 and at most {N(max)}{unit}.");
    }

    private static string N(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A one-line summary for the OEM Specs list ("Rate 57–63 ppm · Ratio 60–70%"), or null
    /// when the item has no spec.</summary>
    public static string? Summary(EquipmentItem item)
    {
        var parts = new List<string>();
        if (OemSpecs.Pulsator(item) is { } p)
        {
            if (p.RateMin is not null && p.RateMax is not null) parts.Add($"Rate {N(p.RateMin.Value)}–{N(p.RateMax.Value)} ppm");
            if (p.RatioMin is not null && p.RatioMax is not null) parts.Add($"Ratio {N(p.RatioMin.Value)}–{N(p.RatioMax.Value)}%");
        }
        else if (OemSpecs.VacuumPump(item) is { } v)
        {
            if (v.AirFlow is not null) parts.Add($"{N(v.AirFlow.Value)} L/min per rpm");
            if (v.MinRpm is not null && v.MaxRpm is not null) parts.Add($"{N(v.MinRpm.Value)}–{N(v.MaxRpm.Value)} rpm");
        }
        else
        {
            return null;
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : "Spec entered";
    }
}
