using System.Text.Json;

namespace Autorep.Web.Services;

// Wraps NZ Post's keyless legacy address-finder endpoints — the same ones behind
// nzpost.co.nz/tools/address-postcode-finder. No API key required. Called server-side
// only (online, from the admin Farm Details screens) so the spoofed browser headers and
// the upstream request stay off the client. Adapted from a prior Pedersen Group project.
//
// Note: this is an unofficial / legacy endpoint and could change without notice. If it
// becomes unreliable, swap this class for NZ Post's official AddressChecker API — the two
// proxy endpoints (suggest, details) are the only call sites.
public class NzPostAddressClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NzPostAddressClient> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NzPostAddressClient(HttpClient http, ILogger<NzPostAddressClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<NzPostSuggestResponse> SuggestAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
            return new NzPostSuggestResponse { Success = false, Status = "invalid", Error = "Query too short" };

        var url = $"https://tools.nzpost.co.nz/legacy/api/suggest?q={Uri.EscapeDataString(query)}&MaxData=max%3A10";
        return await SendAsync<NzPostSuggestResponse>(url, ct)
            ?? new NzPostSuggestResponse { Success = false, Status = "error", Error = "NZ Post suggest unavailable" };
    }

    public async Task<NzPostDetailsResponse> DetailsAsync(long dpid, CancellationToken ct = default)
    {
        var url = $"https://tools.nzpost.co.nz/legacy/api/details?dpid={dpid}";
        return await SendAsync<NzPostDetailsResponse>(url, ct)
            ?? new NzPostDetailsResponse { Success = false };
    }

    private async Task<T?> SendAsync<T>(string url, CancellationToken ct) where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Origin", "https://www.nzpost.co.nz");
            request.Headers.Add("Referer", "https://www.nzpost.co.nz/tools/address-postcode-finder");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NZ Post address lookup failed: {StatusCode}", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NZ Post address lookup error");
            return null;
        }
    }
}
