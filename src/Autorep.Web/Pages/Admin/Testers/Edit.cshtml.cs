using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Testers;

public class EditModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly AutorepDbContext _db;
    private readonly IEmailSender _email;
    private readonly RefreshTokenService _refresh;

    public EditModel(
        UserManager<Tester> users,
        AutorepDbContext db,
        IEmailSender email,
        RefreshTokenService refresh)
    {
        _users = users;
        _db = db;
        _email = email;
        _refresh = refresh;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public DetailsModel Details { get; set; } = new();

    [BindProperty]
    public DateOnly? LicenceExpiryDate { get; set; }

    // Named EditingUser to avoid shadowing PageModel.User (the current principal).
    public Tester? EditingUser { get; private set; }
    public string PrimaryRole { get; private set; } = "—";
    public string? CompanyName { get; private set; }
    public List<SelectListItem> Companies { get; private set; } = [];
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public class DetailsModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public Guid? TestingCompanyId { get; set; }
        public string Role { get; set; } = Roles.Tester;
        public string? CertificateNo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync(hydrateForGet: true)) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostDetailsAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        EditingUser!.DisplayName = Details.DisplayName.Trim();
        EditingUser.TestingCompanyId = Details.TestingCompanyId;
        EditingUser.CertificateNo = string.IsNullOrWhiteSpace(Details.CertificateNo) ? null : Details.CertificateNo.Trim();
        await _users.UpdateAsync(EditingUser);

        var currentRoles = await _users.GetRolesAsync(EditingUser);
        await _users.RemoveFromRolesAsync(EditingUser, currentRoles);
        await _users.AddToRoleAsync(EditingUser, Details.Role);

        Message = "Details saved.";
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    public async Task<IActionResult> OnPostLicenceAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        EditingUser!.LicenceExpiryDate = LicenceExpiryDate;
        await _users.UpdateAsync(EditingUser);
        Message = LicenceExpiryDate is null
            ? "Licence cleared."
            : $"Licence set to expire {LicenceExpiryDate:dd MMM yyyy}.";
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        var token = await _users.GeneratePasswordResetTokenAsync(EditingUser!);
        var resetUrl = Url.Page("/Account/ResetPassword", null,
            new { email = EditingUser!.Email, token }, Request.Scheme);
        await _email.SendEmailAsync(EditingUser.Email!,
            "Reset your NZMPTA AutoRep password",
            $"<p>A password reset has been requested for your account. " +
            $"<a href=\"{resetUrl}\">Click here to set a new password</a>. " +
            $"The link is valid for 1 hour.</p>");
        Message = "Password reset link sent.";
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    public async Task<IActionResult> OnPostForceLogoutAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        // Bump the security stamp so all existing auth cookies become invalid.
        await _users.UpdateSecurityStampAsync(EditingUser!);
        // Revoke all active refresh tokens.
        await _refresh.RevokeAllAsync(EditingUser!.Id, "force-logout");
        Message = "User forced out of all sessions.";
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        if (EditingUser!.LockoutEnd.HasValue && EditingUser.LockoutEnd > DateTimeOffset.UtcNow)
        {
            await _users.SetLockoutEndDateAsync(EditingUser, null);
            Message = "User reactivated.";
        }
        else
        {
            await _users.SetLockoutEndDateAsync(EditingUser, DateTimeOffset.UtcNow.AddYears(100));
            await _refresh.RevokeAllAsync(EditingUser.Id, "deactivated");
            Message = "User deactivated.";
        }
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    private async Task<bool> LoadAsync(bool hydrateForGet)
    {
        EditingUser = await _db.Users
            .Include(u => u.TestingCompany)
            .FirstOrDefaultAsync(u => u.Id == Id);
        if (EditingUser is null) return false;

        var roles = await _users.GetRolesAsync(EditingUser);
        PrimaryRole = roles.FirstOrDefault() ?? "—";
        CompanyName = EditingUser.TestingCompany?.Name;

        if (hydrateForGet)
        {
            Details.DisplayName = EditingUser.DisplayName;
            Details.TestingCompanyId = EditingUser.TestingCompanyId;
            Details.Role = PrimaryRole == "—" ? Roles.Tester : PrimaryRole;
            Details.CertificateNo = EditingUser.CertificateNo;
            LicenceExpiryDate = EditingUser.LicenceExpiryDate;
        }

        Companies = await _db.TestingCompanies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();

        return true;
    }
}
