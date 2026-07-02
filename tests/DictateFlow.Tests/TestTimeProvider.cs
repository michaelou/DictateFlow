namespace DictateFlow.Tests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/>: the timestamp only moves when
/// <see cref="Advance"/> is called (lets tests simulate silence without waiting), the wall
/// clock is pinned to <see cref="UtcNow"/>, and the local zone defaults to UTC but can be
/// overridden for local-time boundary tests.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private long _timestamp;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _timestamp;

    /// <summary>Gets or sets the fixed wall-clock instant returned by <see cref="GetUtcNow"/>.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc));

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <summary>Gets or sets the time zone used for local-time conversions; UTC by default.</summary>
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => Zone;

    /// <summary>Moves the monotonic clock forward by <paramref name="duration"/>.</summary>
    /// <param name="duration">The amount of simulated time to elapse.</param>
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}
