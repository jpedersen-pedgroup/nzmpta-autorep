using System.Net;
using System.Text.Json;
using Autorep.Web.Domain;
using FluentAssertions;

namespace Autorep.Web.Tests;

/// <summary>
/// The header's account circle shows two initials from the person's name (Josh, 13 Nov 2026), or the
/// first letter of the sign-in email when there's no name. The rule is pinned to the shared fixtures
/// (tests/fixtures/initials), which Client/shell/initials.test.ts also runs, so the offline shell
/// draws the same letters. The name reaches the header as a sign-in claim, so that part goes through
/// the real cookie pipeline.
/// </summary>
public class UserInitialsTests : IClassFixture<CookieAuthWebAppFactory>
{
    private const string Password = "Correct-Horse-Battery-1";
    private readonly CookieAuthWebAppFactory _factory;

    public UserInitialsTests(CookieAuthWebAppFactory factory) => _factory = factory;

    private sealed record Case(string Name, string? DisplayName, string? UserName, string Expected);

    [Fact]
    public void Initials_match_all_shared_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Initials", "cases.json");
        var cases = JsonSerializer.Deserialize<Case[]>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        cases.Should().NotBeEmpty();
        foreach (var c in cases)
            UserInitials.For(c.DisplayName, c.UserName).Should().Be(c.Expected, c.Name);
    }

    [Theory]
    [InlineData("Fern Fieldtester", "FF")]
    [InlineData("", "Z")] // no name: the first letter of the email, which starts "zed-"
    public async Task The_header_shows_the_signed_in_persons_initials(string displayName, string expected)
    {
        var email = $"zed-{Guid.NewGuid():N}@example.test";
        await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator],
            configure: u => u.DisplayName = displayName);
        var browser = _factory.CreateBrowser();
        await browser.LoginAsync(email, Password);

        var res = await browser.GetAsync("/Admin");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
        html.Should().Contain($"aria-hidden=\"true\">{expected}</span>")
            .And.Contain($"title=\"{email}\"", "the email is still the tooltip");
    }
}
