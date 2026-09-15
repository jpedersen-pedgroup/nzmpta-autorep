namespace Autorep.Web.Domain.Entities;

/// <summary>
/// One vacuum pump on a <see cref="MachineConfiguration"/> — the legacy page-2 row: make, model,
/// motor size, whether it also drives the milk pump, and the regulator type. Positional: pump N
/// lines up with the pump-N readings (8a–8c). Stored as JSON on the configuration row; the device
/// captures it and the server stores and returns it as sent.
/// </summary>
public sealed record VacuumPumpDetail(
    string? Make = null, string? Model = null, string? MotorSize = null,
    bool DrivesMilkPump = false, string? RegulatorType = null);

/// <summary>One releaser (milk) pump on a <see cref="MachineConfiguration"/>: make, model, motor size.</summary>
public sealed record ReleaserPumpDetail(string? Make = null, string? Model = null, string? MotorSize = null);
