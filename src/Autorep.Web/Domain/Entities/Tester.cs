using Microsoft.AspNetCore.Identity;

namespace Autorep.Web.Domain.Entities;

// The platform's user/identity entity. Extends IdentityUser so we get
// email/password auth, lockout, two-factor etc. for free. Application roles
// (Tester / CompanyAdministrator / SuperAdministrator) live in the Identity
// roles store, not on this class.
public class Tester : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public Guid? TestingCompanyId { get; set; }
    public TestingCompany? TestingCompany { get; set; }

    // Tester Licence (Phase 8). Nullable so newly-created Testers start with
    // no licence until a Super-Administrator sets one. Login pipeline refuses
    // sign-in if expired.
    public DateOnly? LicenceExpiryDate { get; set; }

    // Set on migrated accounts so first sign-in forces a password change. Set
    // back to false after the reset. The Migration Tool (Phase 11) sets this
    // true on every migrated row so legacy passwords are never trusted.
    public bool ForcedPasswordResetRequired { get; set; }

    // Tester accreditation / certificate number (legacy Users.CertificateNo),
    // shown on the report sign-off block. Migrated from the legacy system.
    public string? CertificateNo { get; set; }

    // Terms of Use acceptance. The login gate requires (re-)acceptance when the accepted version
    // differs from the current PrivacyContent.TermsVersion, OR when the licence has been renewed
    // since acceptance (TermsAcceptedLicenceExpiry != LicenceExpiryDate).
    public string? TermsAcceptedVersion { get; set; }
    public DateTimeOffset? TermsAcceptedAt { get; set; }
    public DateOnly? TermsAcceptedLicenceExpiry { get; set; }
}
