namespace AgenticSdlc.Api.Tests;

/// <summary>
/// A clock frozen at <paramref name="utcNow"/> (spec 2026-10-06-user-birth-date §5), so date boundaries
/// never depend on the real clock. <paramref name="localTimeZone"/> lets a test prove "today" is the UTC date.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? TimeZoneInfo.Utc;
}
