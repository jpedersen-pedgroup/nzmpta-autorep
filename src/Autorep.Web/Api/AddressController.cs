using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autorep.Web.Api;

// Server-side proxy for NZ Post address autocomplete on the admin Farm Details screens.
// The keyless NZ Post legacy endpoint isn't called from the browser (CORS + tidiness);
// the browser hits these endpoints, which are admin-only and online by nature.
[ApiController]
[Route("api/address")]
[Authorize(Policy = "AdminArea")]
public class AddressController : ControllerBase
{
    private readonly NzPostAddressClient _nzPost;
    public AddressController(NzPostAddressClient nzPost) => _nzPost = nzPost;

    public record Suggestion(long Dpid, string FullAddress);
    public record AddressDetail(string? AddressLine1, string? AddressLine2, string? Town, string? PostCode);

    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        var res = await _nzPost.SuggestAsync(q ?? "", ct);
        var items = res.Addresses
            .Where(a => !string.IsNullOrWhiteSpace(a.FullAddress))
            .Select(a => new Suggestion(a.DPID, a.FullAddress!))
            .ToList();
        return Ok(items);
    }

    [HttpGet("details")]
    public async Task<IActionResult> Details([FromQuery] long dpid, CancellationToken ct)
    {
        var res = await _nzPost.DetailsAsync(dpid, ct);
        var d = res.Details.FirstOrDefault();
        if (d is null) return NotFound();

        // AddressLine3 is "City Postcode" (e.g. "Wellington 6011"); strip the trailing
        // postcode to get the town. Region (one of the 16 NZ regions) is not returned by
        // NZ Post, so it stays a manual selection on the form.
        var town = d.AddressLine3;
        if (!string.IsNullOrWhiteSpace(town) && !string.IsNullOrWhiteSpace(d.Postcode))
            town = town.Replace(d.Postcode, "").Trim();

        return Ok(new AddressDetail(
            d.AddressLine1,
            d.AddressLine2,
            string.IsNullOrWhiteSpace(town) ? null : town,
            d.Postcode));
    }
}
