namespace Autorep.Web.Domain;

// Timestamps are stored as UTC instants (DateTimeOffset). Admin pages are server-rendered for
// staff in New Zealand, and the production host runs in UTC — so ToLocalTime()/raw rendering
// shows the wrong wall time (and near midnight, the wrong date). Rendering must pin the NZ
// time zone explicitly. Tester-facing lists are client-rendered and already use device time.
public static class NzTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // The IANA id resolves on Linux and on Windows via ICU; the Windows id is the fallback
        // for hosts without ICU time-zone data.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("New Zealand Standard Time"); }
    }

    /// <summary>The given instant expressed in New Zealand local time.</summary>
    public static DateTimeOffset ToNz(this DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>Today's date in New Zealand — NOT the server's (UTC) date, which lags NZ by up to 13 hours.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToNz().DateTime);
}
