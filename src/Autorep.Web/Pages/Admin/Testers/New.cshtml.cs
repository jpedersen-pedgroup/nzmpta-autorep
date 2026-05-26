using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Testers;

public class NewModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly AutorepDbContext _db;
    private readonly IEmailSender _email;

    public NewModel(UserManager<Tester> users, AutorepDbContext db, IEmailSender email)
    {
        _users = users;
        _db = db;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();
    public List<SelectListItem> Companies { get; private set; } = [];

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Guid? TestingCompanyId { get; set; }
        public string Role { get; set; } = Roles.Tester;
        public DateOnly? LicenceExpiryDate { get; set; }
    }

    public async Task OnGetAsync() => await PopulateAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await PopulateAsync();
        if (string.IsNullOrWhiteSpace(Input.Email)) Errors.Add("Email is required.");
        if (string.IsNullOrWhiteSpace(Input.DisplayName)) Errors.Add("Display name is required.");
        if (!Roles.All.Contains(Input.Role)) Errors.Add("Role is invalid.");
        if (Input.Role == Roles.Tester && Input.TestingCompanyId is null)
            Errors.Add("Testers must be assigned to a Testing Company.");
        if (Errors.Any()) return Page();

        // Set a temporary random password; user is sent a reset link to set their own.
        var tempPassword = "Temp-" + Guid.NewGuid().ToString("N").Substring(0, 16) + "!9";
        var user = new Tester
        {
            UserName = Input.Email,
            Email = Input.Email,
            EmailConfirmed = false,
            DisplayName = Input.DisplayName.Trim(),
            TestingCompanyId = Input.TestingCompanyId,
            LicenceExpiryDate = Input.LicenceExpiryDate,
            ForcedPasswordResetRequired = true
        };
        var create = await _users.CreateAsync(user, tempPassword);
        if (!create.Succeeded)
        {
            foreach (var e in create.Errors) Errors.Add(e.Description);
            return Page();
        }
        await _users.AddToRoleAsync(user, Input.Role);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Page("/Account/ResetPassword", null,
            new { email = user.Email, token }, Request.Scheme);
        await _email.SendEmailAsync(user.Email!,
            "Welcome to NZMPTA AutoRep — set your password",
            $"<p>Hi {user.DisplayName},</p>" +
            $"<p>An NZMPTA AutoRep account has been created for you. Click the link below to set your password and sign in.</p>" +
            $"<p><a href=\"{resetUrl}\">Set your password</a></p>");

        return RedirectToPage("/Admin/Testers/Index");
    }

    private async Task PopulateAsync()
    {
        Companies = await _db.TestingCompanies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();
    }
}
