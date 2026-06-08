namespace Autorep.Web.Domain.Entities;

/// <summary>
/// Milking-plant layout. Drives standards calculations and which Visual-Faults running
/// sub-sections apply (Rotary rotation vs Herringbone bail area).
/// </summary>
public enum PlantType
{
    HerringboneLowline = 0,
    HerringboneHighline = 1,
    Rotary = 2,
    Other = 99,
}

/// <summary>How the vacuum pump is lubricated — drives the ISO group 9 oil/water flow-rate check.</summary>
public enum PumpLubrication
{
    OilLubricated = 0,
    LiquidRing = 1,
    Other = 99,
}

/// <summary>
/// The Machine Configuration a Tester declares upfront (the legacy "Farm &amp; Milking Machine"
/// two-page form). It is the sole input to the <see cref="Wizard.WizardStepResolver"/> — it
/// determines which Wizard Steps and sub-sections apply — and it feeds the standards used by the
/// Pass/Fail Calculator. One per <see cref="MachineTest"/>.
/// </summary>
public class MachineConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Machine Test this configuration belongs to (1:1).</summary>
    public Guid MachineTestId { get; set; }

    // --- Plant ------------------------------------------------------------
    public PlantType PlantType { get; set; } = PlantType.HerringboneLowline;

    /// <summary>"Plant size" = number of clusters. Mandatory; drives effective-reserve and
    /// air-consumption standards (the legacy app refuses to calculate without it).</summary>
    public int ClusterCount { get; set; }

    public int? HerdSize { get; set; }

    /// <summary>Result of the last Bulk Milk Cell Count, recorded for the report.</summary>
    public string? LastBmcc { get; set; }

    /// <summary>Milkline bore (e.g. "100mm"); feeds the cleaning-reserve standard.</summary>
    public string? MilklineSize { get; set; }

    /// <summary>A flushing pulsation system selects cleaning-reserve vs effective-reserve as the
    /// governing standard (mandatory in the legacy app for that reason).</summary>
    public bool FlushingPulsationSystem { get; set; }

    // --- Pulsation & cluster ---------------------------------------------
    public string? PulsatorModel { get; set; }
    public int PulsatorCount { get; set; }
    public string? ClawModel { get; set; }
    public string? ShellModel { get; set; }
    public string? LinerModel { get; set; }

    /// <summary>Vented liners change the cluster-air-admission pass band.</summary>
    public bool LinerVented { get; set; }

    // --- Vacuum system ----------------------------------------------------
    public int NumberOfVacuumPumps { get; set; } = 1;
    public PumpLubrication PumpLubrication { get; set; } = PumpLubrication.OilLubricated;

    /// <summary>Variable-speed-drive fitted — adds the minimum-pump-speed vacuum readings.</summary>
    public bool VsdFitted { get; set; }

    /// <summary>Whether ISO test ports (A1/A2/A3, Vm/Vr/Vp) are available. When false the Tester
    /// runs the reduced "short test".</summary>
    public bool IsoPortsAvailable { get; set; } = true;

    /// <summary>A pulsator-stop system changes the reserve / pump test path.</summary>
    public bool HasPulsatorStopSystem { get; set; }

    // --- Ancillary equipment (drives Additional-Tests sub-sections) -------
    public bool HasAcr { get; set; }
    public bool HasBailGates { get; set; }
    public bool HasMilkMeters { get; set; }
    public bool HasTeatSprayer { get; set; }
    public bool HasBackingGate { get; set; }
    public bool HasReleaserPump { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public MachineTest? MachineTest { get; set; }

    public bool IsRotary => PlantType == PlantType.Rotary;
    public bool IsHerringbone => PlantType is PlantType.HerringboneHighline or PlantType.HerringboneLowline;
}
