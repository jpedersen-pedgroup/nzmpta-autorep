using Autorep.Web.Domain;
using FluentAssertions;

namespace Autorep.Web.Tests;

// Admin pages render UTC instants in New Zealand time via NzTime, because the production host
// runs in UTC — ToLocalTime() there is a no-op and raw values can even show the previous date.
public class NzTimeTests
{
    [Fact]
    public void A_winter_instant_renders_at_utc_plus_12()
    {
        var instant = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var nz = instant.ToNz();
        nz.Offset.Should().Be(TimeSpan.FromHours(12));
        nz.ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("15 Jun 2026, 12:00");
    }

    [Fact]
    public void A_summer_instant_renders_at_utc_plus_13()
    {
        var instant = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var nz = instant.ToNz();
        nz.Offset.Should().Be(TimeSpan.FromHours(13));
        nz.ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("15 Jan 2026, 13:00");
    }

    [Fact]
    public void A_late_utc_evening_lands_on_the_next_nz_calendar_day()
    {
        // The reported bug: a test signed off 27 Aug 2026 11:17 NZST displayed as "26 Aug 2026, 23:17".
        var instant = new DateTimeOffset(2026, 8, 26, 23, 17, 0, TimeSpan.Zero);
        instant.ToNz().ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("27 Aug 2026, 11:17");
    }

    [Fact]
    public void The_spring_forward_gap_maps_into_nzdt()
    {
        // NZDT starts 27 Sep 2026: 02:00 NZST jumps to 03:00, so 02:30 local does not exist —
        // the instant lands at 03:30 (+13) rather than in the gap.
        var instant = new DateTimeOffset(2026, 9, 26, 14, 30, 0, TimeSpan.Zero);
        var nz = instant.ToNz();
        nz.Offset.Should().Be(TimeSpan.FromHours(13));
        nz.ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture).Should().Be("27 Sep 2026, 03:30");
    }
}
