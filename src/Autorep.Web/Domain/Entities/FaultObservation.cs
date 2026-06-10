namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A standard fault observation for one visual check: the dropdown wording the Tester picks when
/// a check is marked Fault, with its CMM severity and default recommendation. Category is the
/// check's lookup key (e.g. "VPWickCondition") — the wizard's checklist items reference it.
/// Seeded from the legacy Lookup catalog merged with the CMM ratings; SuperAdmin-managed and
/// synced to Devices (bundled defaults are the offline fallback).
/// </summary>
public class FaultObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The visual check's lookup category (ChecklistItem.lookup in the wizard).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The standard fault wording shown in the dropdown.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Critical | Major | Minor (CMM).</summary>
    public string Severity { get; set; } = "Major";

    /// <summary>Default recommendation pre-filled on the Fault Summary (Tester-editable).</summary>
    public string? Recommendation { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
