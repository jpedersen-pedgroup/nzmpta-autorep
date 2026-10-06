using Autorep.Web.Domain;

namespace Autorep.Web.Services;

/// <summary>
/// Keeps an account that must use two-factor, but hasn't set it up, on the set-up page. Runs
/// after authentication (it needs the principal) and before authorization, so it applies to
/// pages and API controllers alike: pages are redirected, <c>/api/*</c> answers 403 with a
/// reason, because a redirect to HTML is exactly what <c>fetch</c> mishandles.
/// </summary>
public class MfaEnrolmentMiddleware(RequestDelegate next)
{
    public const string SetupPath = "/Account/SetupAuthenticator";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && MfaPolicy.MustEnrol(ctx.User)
            && !MfaPolicy.IsAllowedWhileUnenrolled(ctx.Request.Path))
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

        await next(ctx);
    }
}
