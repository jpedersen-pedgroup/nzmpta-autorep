namespace Autorep.Web.Pages.Admin;

/// <summary>View model for the shared _CompanyLogoField partial — the testing-company logo
/// preview + upload/remove field on both the Super-Admin company editor and the Company
/// Administrator's "My company" page. Both bind the posted file as Input.Logo and the remove
/// checkbox as Input.RemoveLogo.</summary>
public record CompanyLogoField(Guid CompanyId, string CompanyName, bool HasLogo, bool Printable, string? Version);
