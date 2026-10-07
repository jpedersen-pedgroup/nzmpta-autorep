using Autorep.Web.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Tests;

/// <summary>
/// The "trust this device" cookie's lifetime is a hard 30 days (PRD: the code is asked for again
/// every 30 days). A time-travel test isn't practical, so pin the options Identity will use:
/// the lifetime, and sliding expiration OFF - with it on, Identity re-issues the cookie whenever
/// it is used past half-life, so a device in regular use would never be challenged again.
/// </summary>
public class TrustedDeviceCookieOptionsTests : IClassFixture<CookieAuthWebAppFactory>
{
    private readonly CookieAuthWebAppFactory _factory;
    public TrustedDeviceCookieOptionsTests(CookieAuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public void Trusted_device_cookie_lasts_30_days_and_does_not_slide()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.TwoFactorRememberMeScheme);

        options.ExpireTimeSpan.Should().Be(MfaPolicy.TrustedDeviceLifetime);
        options.SlidingExpiration.Should().BeFalse("a trusted device must be re-challenged after 30 days however often it is used");
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SecurePolicy.Should().Be(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always);
    }
}
