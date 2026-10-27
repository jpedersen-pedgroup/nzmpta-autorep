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

    /// <summary>The Change company dialog's choice; null moves the user out of any company (allowed
    /// only for roles that don't need one).</summary>
    [BindProperty]
    public Guid? NewCompanyId { get; set; }

    // Named EditingUser to avoid shadowing PageModel.User (the current principal).
    public Tester? EditingUser { get; private set; }
    public string RolesDisplay { get; private set; } = "—";
    public string? CompanyName { get; private set; }
    /// <summary>The user's stored roles need a Testing Company (Tester, Company Administrator).</summary>
    public bool NeedsCompany { get; private set; }
    /// <summary>Active companies the user could move to (their current one left out).</summary>
    public List<SelectListItem> Companies { get; private set; } = [];
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    // The company isn't part of Details: changing it is its own, confirmed step (ChangeCompany).
    public class DetailsModel
    {
        public string DisplayName { get; set; } = string.Empty;
        // A user may hold more than one role (e.g. Company Administrator + Tester).
        public List<string> SelectedRoles { get; set; } = new();
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

        var roles = Details.SelectedRoles.Distinct().ToList();
        if (roles.Count == 0) Errors.Add("Select at least one role.");
        if (roles.Any(r => !Roles.All.Contains(r))) Errors.Add("Role is invalid.");
        if (Roles.CompanyRequiredError(roles, EditingUser!.TestingCompanyId) is { } noCompany)
            Errors.Add($"{noCompany} Give them one with Change company first.");
        if (Errors.Any()) return Page(); // LoadAsync populated Companies; Details keeps the posted values

        var currentRoles = await _users.GetRolesAsync(EditingUser!);
        // Never let the system's last Super Administrator be demoted (incl. self-demotion via the
        // multi-role checkboxes) — that would lock everyone out of the SuperAdmin-only admin areas.
        if (currentRoles.Contains(Roles.SuperAdministrator) && !roles.Contains(Roles.SuperAdministrator))
        {
            var superAdmins = await _users.GetUsersInRoleAsync(Roles.SuperAdministrator);
            if (superAdmins.All(u => u.Id == EditingUser!.Id))
            {
                Errors.Add("This is the only Super Administrator — grant the role to someone else before removing it here.");
                return Page();
            }
        }

        EditingUser!.DisplayName = Details.DisplayName.Trim();
        EditingUser.CertificateNo = string.IsNullOrWhiteSpace(Details.CertificateNo) ? null : Details.CertificateNo.Trim();
        await _users.UpdateAsync(EditingUser);

        await _users.RemoveFromRolesAsync(EditingUser, currentRoles);
        await _users.AddToRolesAsync(EditingUser, roles);

        Message = "Details saved.";
        await LoadAsync(hydrateForGet: true);
        return Page();
    }

    /// <summary>Moves the user to another Testing Company (or, when their roles allow it, out of one).
    /// What they did for the old company stays with it: each test keeps the company stamped when it
    /// was first sent, and farms are in a company's scope through those stamps (FarmScope), so the
    /// farms they tested stay behind. Takes effect at once: there's no company claim to go stale.</summary>
    public async Task<IActionResult> OnPostChangeCompanyAsync()
    {
        if (!await LoadAsync(hydrateForGet: true)) return NotFound();

        var fromName = CompanyName;
        if (NewCompanyId == EditingUser!.TestingCompanyId)
        {
            Errors.Add(fromName is null ? "They aren't in a company already." : $"They're already with {fromName}.");
            return Page();
        }
        string? toName = null;
        if (NewCompanyId is not null)
        {
            toName = await _db.TestingCompanies.Where(c => c.Id == NewCompanyId && c.IsActive)
                .Select(c => c.Name).FirstOrDefaultAsync();
            if (toName is null)
            {
                Errors.Add("Choose an active Testing Company.");
                return Page();
            }
        }
        if (Roles.CompanyRequiredError(await _users.GetRolesAsync(EditingUser), NewCompanyId) is { } noCompany)
        {
            Errors.Add(noCompany);
            return Page();
        }

        EditingUser.TestingCompanyId = NewCompanyId;
        await _users.UpdateAsync(EditingUser);

        Message = (fromName, toName) switch
        {
            (null, _) => $"Added to {toName}.",
            (_, null) => $"Removed from {fromName}. The tests and farms they did for {fromName} stay with it.",
            _ => $"Moved to {toName}. The tests and farms they did for {fromName} stay with {fromName}.",
        };
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
            "Reset your MPNZ AutoRep password",
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

    /// <summary>The way back in for someone who has lost both the authenticator app and their
    /// recovery codes. Clears the enrolment and ends every session and trusted device: the next
    /// sign-in is password only (a Super Administrator is then made to enrol again at once).</summary>
    public async Task<IActionResult> OnPostResetTwoFactorAsync()
    {
        if (!await LoadAsync(hydrateForGet: false)) return NotFound();
        await _users.SetTwoFactorEnabledAsync(EditingUser!, false);
        await _users.ResetAuthenticatorKeyAsync(EditingUser!);
        // Invalidates auth cookies AND the "trust this device" cookies, which carry the stamp too.
        await _users.UpdateSecurityStampAsync(EditingUser!);
        await _refresh.RevokeAllAsync(EditingUser!.Id, "two-factor-reset");
        Message = "Two-factor reset. Open sessions end within a minute; their next sign-in needs the password only, and a Super Administrator will be asked to set up a new authenticator straight away.";
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
            await _users.SetLockoutEndDateAsync(EditingUser, AccountLockout.DeactivatedUntil());
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
        RolesDisplay = roles.Count == 0 ? "—" : string.Join(", ", roles.Select(Roles.Label));
        CompanyName = EditingUser.TestingCompany?.Name;
        NeedsCompany = Roles.CompanyRequiredError(roles, testingCompanyId: null) is not null;

        if (hydrateForGet)
        {
            Details.DisplayName = EditingUser.DisplayName;
            Details.SelectedRoles = roles.Count > 0 ? roles.ToList() : new List<string> { Roles.Tester };
            Details.CertificateNo = EditingUser.CertificateNo;
            LicenceExpiryDate = EditingUser.LicenceExpiryDate;
        }

        var currentCompanyId = EditingUser.TestingCompanyId;
        Companies = await _db.TestingCompanies
            .Where(c => c.IsActive && c.Id != currentCompanyId)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();

        return true;
    }
}
