using System.Security.Claims;

namespace Autorep.Web.Domain;

/// <summary>
/// The side of AutoRep a user who is both a Tester and an administrator (Company or Super) is
/// working in: the tester app (/App) or the admin portal (/Admin). They're asked at sign-in
/// (/Account/ChooseRole, Josh 12 Nov 2026) and can switch from the menu bar. It decides the menu,
/// where "/" lands, and the tester-only header items (licence reminder, connection status).
///
/// It is NOT a permission. Both roles stay on the account, so a link into the other side (a
/// farm-review email into /Admin, say) still opens, and the menu follows the page. Users with
/// only one side never see any of it.
/// </summary>
public enum SignInRole { Tester, Admin }

public static class SignInRoles
{
    /// <summary>Remembers the side for "/" and the shared pages (Help, Account). Forgotten when
    /// the sign-in form is shown and at sign-out, so every sign-in asks again.</summary>
    public const string CookieName = "autorep-signin-role";

    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdministrator) || user.IsInRole(Roles.CompanyAdministrator);

    public static bool HasBoth(ClaimsPrincipal user) => user.IsInRole(Roles.Tester) && IsAdmin(user);

    /// <summary>The side a page belongs to, or null for the shared pages and "/".</summary>
    public static SignInRole? ForPath(PathString path) =>
        path.StartsWithSegments("/App", StringComparison.OrdinalIgnoreCase) ? SignInRole.Tester
        : path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase) ? SignInRole.Admin
        : null;

    public static SignInRole? Remembered(HttpRequest request) => request.Cookies[CookieName] switch
    {
        "tester" => SignInRole.Tester,
        "admin" => SignInRole.Admin,
        _ => null,
    };

    /// <summary>The side the current page is drawn for: the page's own side, else the remembered
    /// one, else the admin portal (where "/" sent everyone with an admin role before this).</summary>
    public static SignInRole Current(HttpContext context)
    {
        var user = context.User;
        if (!HasBoth(user)) return user.IsInRole(Roles.Tester) ? SignInRole.Tester : SignInRole.Admin;
        return ForPath(context.Request.Path) ?? Remembered(context.Request) ?? SignInRole.Admin;
    }

    public static string HomePath(SignInRole role) => role == SignInRole.Tester ? "/App" : "/Admin";

    /// <summary>What the admin side is called for this user, e.g. "Super Administrator".</summary>
    public static string AdminLabel(ClaimsPrincipal user) =>
        Roles.Label(user.IsInRole(Roles.SuperAdministrator) ? Roles.SuperAdministrator : Roles.CompanyAdministrator);

    /// <summary>The shorter form for the menu bar's switch link, e.g. "Super Admin".</summary>
    public static string AdminShortLabel(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdministrator) ? "Super Admin" : "Company Admin";

    public static void Remember(HttpResponse response, SignInRole role) =>
        response.Cookies.Append(CookieName, role == SignInRole.Tester ? "tester" : "admin", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true, // a menu preference, not tracking
            MaxAge = TimeSpan.FromDays(365), // outlives "Keep me signed in"; sign-in forgets it anyway
        });

    public static void Forget(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, SameSite = SameSiteMode.Lax });

    /// <summary>Middleware: when a user with both sides opens a page on one of them, remember it,
    /// so the menu-bar switch is a plain link and "/" goes back to where they were. Runs after
    /// authorization, so only a page they were allowed onto counts.</summary>
    public static Task RememberFromPageAsync(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            && context.User.Identity?.IsAuthenticated == true
            && ForPath(context.Request.Path) is { } side
            && HasBoth(context.User)
            && Remembered(context.Request) != side)
        {
            Remember(context.Response, side);
        }
        return next(context);
    }
}
