using Autorep.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Autorep.Web.Services;

/// <summary>
/// Two-factor enforcement for the roles that need it. Runs after authentication (it needs the
/// principal and the ticket) and before authorization, so it applies to pages and API controllers
/// alike: pages are redirected, <c>/api/*</c> answers with a status and a reason, because a
/// redirect to HTML is exactly what <c>fetch</c> mishandles.
///
/// 1. An account that must use two-factor but hasn't set it up stays on the set-up page.
/// 2. A required-role session whose last proof of the second factor is older than
///    <see cref="MfaPolicy.TrustedDeviceLifetime"/> is signed out and sent to sign in again - the
///    application cookie slides, so without this an administrator who never signed out would
///    never be challenged again.
/// </summary>
public class MfaEnrolmentMiddleware(RequestDelegate next)
{
    public const string SetupPath = "/Account/SetupAuthenticator";
    public const string ExpiredReason = "mfa-expired";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            if (MfaPolicy.MustEnrol(ctx.User) && !MfaPolicy.IsAllowedWhileUnenrolled(ctx.Request.Path))
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { error = "two-factor-enrolment-required" });
                    return;
                }

                ctx.Response.Redirect(SetupPath);
                return;
            }

            if (MfaPolicy.IsRequiredFor(ctx.User) && SessionIsPastItsProof(ctx))
            {
                await ctx.SignOutAsync(IdentityConstants.ApplicationScheme);
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "two-factor-expired" });
                    return;
                }

                var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
                ctx.Response.Redirect($"/Account/Login?returnUrl={returnUrl}&reason={ExpiredReason}");
                return;
            }
        }

        await next(ctx);
    }

    /// <summary>Reads the default scheme's ticket, which the authentication middleware has already
    /// produced for this request, and checks the stamp <c>TesterSignInManager</c> put in it.</summary>
    private static bool SessionIsPastItsProof(HttpContext ctx)
    {
        var items = ctx.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties?.Items;
        return items is not null && MfaPolicy.SessionExpired(items, DateTimeOffset.UtcNow);
    }
}
