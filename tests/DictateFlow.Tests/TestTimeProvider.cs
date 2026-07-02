namespace DictateFlow.Tests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> whose timestamp only moves when
/// <see cref="Advance"/> is called — lets tests simulate silence without waiting.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private long _timestamp;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _timestamp;

    /// <summary>Moves the clock forward by <paramref name="duration"/>.</summary>
    /// <param name="duration">The amount of simulated time to elapse.</param>
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}
