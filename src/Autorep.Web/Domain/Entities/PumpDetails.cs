namespace Autorep.Web.Domain.Entities;

/// <summary>
/// One vacuum pump on a <see cref="MachineConfiguration"/> — the legacy page-2 row: make, model,
/// motor size and whether it also drives the milk pump. Positional: pump N lines up with the
/// pump-N readings (8a–8c). Stored as JSON on the configuration row; the device captures it and
/// the server stores and returns it as sent. <c>RegulatorType</c> is retired (Sep 2026, see
/// <see cref="RegulatorDetail"/>) but kept so tests captured before then, and older devices still
/// sending it, round-trip unchanged.
/// </summary>
public sealed record VacuumPumpDetail(
    string? Make = null, string? Model = null, string? MotorSize = null,
    bool DrivesMilkPump = false, string? RegulatorType = null);

/// <summary>One releaser (milk) pump on a <see cref="MachineConfiguration"/>: make, model, motor size.</summary>
public sealed record ReleaserPumpDetail(string? Make = null, string? Model = null, string? MotorSize = null);

/// <summary>
/// One line of a <see cref="MachineConfiguration"/>'s regulator list: a free-text type and how many
/// of that type are fitted. A shed can run several regulators, and they don't follow the pumps.
/// </summary>
public sealed record RegulatorDetail(string? Type = null, int? Quantity = null);
