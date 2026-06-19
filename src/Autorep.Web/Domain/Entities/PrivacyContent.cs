namespace Autorep.Web.Domain.Entities;

/// <summary>
/// Single-row store for the admin-editable privacy + terms text: the Tester Terms of Use (shown at
/// the acceptance gate), the IPP3A farmer-data collection notice (shown where farmer details are
/// entered), and the privacy footer line on generated reports. A Super-Administrator edits these in
/// the portal; bumping <see cref="TermsVersion"/> re-triggers tester acceptance. The tester client
/// syncs a copy so the report footer + collection notice work offline.
/// </summary>
public class PrivacyContent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Opaque version string for the terms. Changing it forces every tester to re-accept.</summary>
    public string TermsVersion { get; set; } = string.Empty;

    /// <summary>The Terms of Use body shown on the acceptance page (plain text / simple markup).</summary>
    public string TermsBody { get; set; } = string.Empty;

    /// <summary>IPP3A notice shown wherever farmer personal details are entered.</summary>
    public string CollectionNotice { get; set; } = string.Empty;

    /// <summary>Privacy line printed in the footer of generated reports.</summary>
    public string ReportFooterText { get; set; } = string.Empty;

    /// <summary>Contact point for access/correction requests (shown in notices + footer).</summary>
    public string PrivacyContactEmail { get; set; } = string.Empty;

    /// <summary>URL of the full privacy statement.</summary>
    public string PrivacyStatementUrl { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
