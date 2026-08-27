namespace JobAppManager.TestSupport;

/// <summary>A clock that does not move unless a test moves it.
///
/// This is what turns "assert UpdatedUtc is within a second of now" into "assert UpdatedUtc is
/// exactly this instant", and what lets a test fabricate an application that sat in a stage for
/// eleven days without waiting eleven days or writing rows behind EF's back.</summary>
public sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <param name="utcNow">The instant to start at. Defaults to a fixed, arbitrary date rather
    /// than the real clock, so a test that forgets to pin a date is still reproducible.</param>
    public FixedClock(DateTimeOffset? utcNow = null) =>
        _utcNow = utcNow ?? new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    private TimeZoneInfo _localTimeZone = TimeZoneInfo.Utc;

    /// <summary>Pinned to UTC by default so a test does not change behaviour with the machine's
    /// timezone. Use <see cref="SetLocalTimeZone"/> for a test that is specifically about the
    /// local-vs-UTC distinction.</summary>
    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    public void SetLocalTimeZone(TimeZoneInfo zone) => _localTimeZone = zone;

    /// <summary>Moves the clock forward. Returns the new instant so it can be asserted on.</summary>
    public DateTimeOffset Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

    public DateTimeOffset AdvanceDays(double days) => Advance(TimeSpan.FromDays(days));

    /// <summary>Jumps to a specific instant, for tests about a particular date or hour.</summary>
    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;

    /// <summary>Today on this clock, matching how the repository and editor compute it.</summary>
    public DateOnly Today => DateOnly.FromDateTime(GetLocalNow().DateTime);
}
