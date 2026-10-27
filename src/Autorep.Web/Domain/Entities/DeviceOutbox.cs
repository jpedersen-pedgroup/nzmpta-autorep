namespace Autorep.Web.Domain.Entities;

/// <summary>
/// What one of a tester's devices last said it holds that the server hasn't got: how many tests are
/// still to be sent (Client/sync/outboxReport.ts, reported whenever the device is online). A Super
/// Administrator can't move the tester to another Testing Company while any device last said more
/// than none, because a test first sent after a move is filed under the new company
/// (Pages/Admin/Testers/Edit). One row per tester per device; a status, not a record, so it isn't
/// audited.
/// </summary>
public class DeviceOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TesterId { get; set; } = string.Empty;
    public Tester? Tester { get; set; }

    /// <summary>The device's own random id, kept in its browser storage.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>What the device calls itself ("iPad · Safari"), so an administrator can tell them apart.</summary>
    public string? Device { get; set; }

    /// <summary>Tests on the device with something the server hasn't got: never sent, or changed since.</summary>
    public int UnsentTests { get; set; }

    public DateTimeOffset ReportedAt { get; set; }
}
