using Autorep.Web.Data;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Autorep.Web.Pages.Shared.Components.FarmReviewBanner;

/// <summary>The farms-awaiting-review reminder across the top of every admin page
/// (Pages/Shared/_AdminLayout.cshtml). Renders nothing when there's nothing to remind about.</summary>
public class FarmReviewBannerViewComponent(AutorepDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var due = await FarmReviewReminders.DueForAsync(db, UserClaimsPrincipal, DateTimeOffset.UtcNow);
        return due.Count == 0 ? Content(string.Empty) : View(due);
    }
}
