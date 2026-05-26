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

    // Phase 8 will add TesterLicence + LicenceExpiryDate.
}
