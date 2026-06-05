using System.Text.Json.Serialization;

namespace Autorep.Web.Services;

// Response shapes for NZ Post's keyless legacy address-finder endpoints.
// Top-level keys are lower-case and inner keys PascalCase, so the client
// deserialises with PropertyNameCaseInsensitive = true.

public class NzPostSuggestResponse
{
    public bool Success { get; set; }
    public List<NzPostSuggestion> Addresses { get; set; } = new();
    public string? Status { get; set; }

    // Populated by the client on a failed/short request; not part of the JSON.
    [JsonIgnore] public string? Error { get; set; }
}

public class NzPostSuggestion
{
    public long DPID { get; set; }
    public string? SourceDesc { get; set; }
    public string? FullAddress { get; set; }
}

public class NzPostDetailsResponse
{
    public bool Success { get; set; }
    public List<NzPostAddressDetail> Details { get; set; } = new();
}

public class NzPostAddressDetail
{
    public long DPID { get; set; }
    public string? AddressLine1 { get; set; }   // street
    public string? AddressLine2 { get; set; }   // suburb
    public string? AddressLine3 { get; set; }   // "City Postcode"
    public string? Postcode { get; set; }
}
