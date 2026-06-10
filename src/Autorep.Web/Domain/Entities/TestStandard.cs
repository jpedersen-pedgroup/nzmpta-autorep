namespace Autorep.Web.Domain.Entities;

/// <summary>
/// An admin-editable test standard: either a pass/fail rule for one wizard reading (Kind =
/// atMost / atLeast / between / tolerance, keyed by the reading key, e.g. "tr.airlineDropRR"),
/// or a named parameter inside a formula the app computes (Kind = param, keyed "param.*", e.g.
/// the 10% regulation-loss percentage). Seeded from the NZMPTA manual / ISO 6690 values; the
/// SuperAdmin edits rows here and Devices sync them down — the formulas themselves stay in code,
/// every number in them lives in this table.
/// </summary>
public class TestStandard
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reading key ("tr.airlineDropRR") or parameter key ("param.reserve.lossPct").</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Grouping for the admin screen (e.g. "Vacuum system", "Pulsation").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>atMost | atLeast | between | tolerance | param.</summary>
    public string Kind { get; set; } = "atMost";

    public double? Limit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Target { get; set; }
    public double? Tolerance { get; set; }

    /// <summary>The numeric value for Kind = param.</summary>
    public double? Value { get; set; }

    public string? Unit { get; set; }

    /// <summary>Document citation, e.g. "Manual p40 / ISO D.2.13".</summary>
    public string? SourceRef { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
