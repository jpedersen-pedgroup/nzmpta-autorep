using System.Net;
using System.Text;

namespace Autorep.Web.Tests.E2E;

// Returns canned NZ Post responses so the Playwright test is deterministic and offline
// (no dependency on the live NZ Post endpoint in CI). The live integration is covered by
// the manual smoke record in plans/test-schedule.md.
public class StubNzPostHandler : HttpMessageHandler
{
    private const string Suggest =
        "{\"success\":true,\"addresses\":[{\"DPID\":111,\"SourceDesc\":\"Postal\",\"FullAddress\":\"123 Test Street, Suburbia, Testville 1234\"}],\"status\":\"success\"}";

    private const string Details =
        "{\"success\":true,\"details\":[{\"DPID\":111,\"AddressLine1\":\"123 Test Street\",\"AddressLine2\":\"Suburbia\",\"AddressLine3\":\"Testville 1234\",\"Postcode\":\"1234\"}]}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = (request.RequestUri?.AbsolutePath ?? "").Contains("/details") ? Details : Suggest;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
