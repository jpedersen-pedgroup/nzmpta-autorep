namespace Autorep.Web.Domain.Entities;

/// <summary>
/// The Final Report as the tester signed it off (PRD: FinalReportBlob): the record of the copy the
/// tester's device generated at sign-off and uploaded, so an administrator can download what the
/// farmer was given rather than a regeneration. One per <see cref="MachineTest"/> row — and each
/// version of a test is a row of its own, so each version keeps its own report. The PDF itself is
/// in the PDF store (<see cref="Services.Pdfs.IPdfStore"/>) under <see cref="BlobKey"/>.
/// Written through EF, so every upload leaves an audit entry.
/// </summary>
public class FinalReportBlob
{
    public Guid MachineTestId { get; set; }
    public MachineTest? MachineTest { get; set; }

    /// <summary>Where the PDF is in the final-reports container (Services.Pdfs.PdfKeys.FinalReport).</summary>
    public string BlobKey { get; set; } = string.Empty;

    /// <summary>SHA-256 of the stored PDF, lowercase hex: an identical re-upload is a no-op, and the
    /// copy handed out can be checked against what was received.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>When the server received this copy (server clock) — not when it was signed off,
    /// which is the test's MarkedCompleteAt.</summary>
    public DateTimeOffset StoredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The user id that uploaded it: the tester whose test it is.</summary>
    public string StoredBy { get; set; } = string.Empty;
}
