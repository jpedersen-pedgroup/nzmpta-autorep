using Autorep.Web.Domain;
using Autorep.Web.Services;
using FluentAssertions;

namespace Autorep.Web.Tests;

/// <summary>The two pure halves of the 30-day session rule: what stamp a sign-in gets, and when a
/// stamp counts as expired.</summary>
public class MfaSessionStampTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_sign_in_that_carried_the_code_is_stamped_now()
        => TesterSignInManager.MfaStampFor(mfaClaim: true, rememberedAt: Now.AddDays(-10), Now).Should().Be(Now);

    [Fact]
    public void A_trusted_device_keeps_the_time_its_trust_was_issued()
        => TesterSignInManager.MfaStampFor(mfaClaim: false, rememberedAt: Now.AddDays(-10), Now).Should().Be(Now.AddDays(-10));

    [Fact]
    public void A_password_only_sign_in_has_no_stamp()
        => TesterSignInManager.MfaStampFor(mfaClaim: false, rememberedAt: null, Now).Should().BeNull();

    [Theory]
    [InlineData(-29, false)]
    [InlineData(-31, true)]
    public void A_stamp_expires_after_30_days(int daysAgo, bool expired)
    {
        var items = new Dictionary<string, string?> { [MfaPolicy.SessionStampKey] = Now.AddDays(daysAgo).ToString("o") };
        MfaPolicy.SessionExpired(items, Now).Should().Be(expired);
    }

    [Fact]
    public void No_stamp_means_no_deadline_but_garbage_means_stale()
    {
        MfaPolicy.SessionExpired(new Dictionary<string, string?>(), Now).Should().BeFalse();
        MfaPolicy.SessionExpired(new Dictionary<string, string?> { [MfaPolicy.SessionStampKey] = "yesterday-ish" }, Now).Should().BeTrue();
    }
}
