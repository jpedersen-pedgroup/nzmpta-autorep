namespace Autorep.Web.Domain.Entities;

/// <summary>
/// One version of an <see cref="EquipmentItem"/>'s OEM spec, in effect from <see cref="EffectiveFrom"/>
/// until the next version's date. A test is judged against the version in effect on the day it was
/// started, so the SuperAdmin can enter a change ahead of time and devices switch to it on the day,
/// offline or not. History doesn't change: a version can only be dated today or later, and only a
/// version not yet in effect can be replaced or removed.
/// </summary>
public class EquipmentSpecVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentItemId { get; set; }

    /// <summary>The New Zealand date this version applies from. Null for the version seeded from the
    /// legacy specs, which applies from the start - including to every test signed off before specs
    /// could be edited.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>A <see cref="PulsatorSpec"/> or <see cref="VacuumPumpSpec"/> as JSON (<see cref="OemSpecs"/>),
    /// or null: from this date the model has no spec, so no spec verdict.</summary>
    public string? SpecJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
