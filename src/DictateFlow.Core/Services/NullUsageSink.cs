using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services;

/// <summary>
/// No-op <see cref="IUsageSink"/>, registered until M6 adds SQLite persistence and cost math.
/// </summary>
public sealed class NullUsageSink : IUsageSink
{
    /// <inheritdoc />
    public void Record(UsageRecord record)
    {
        // Intentionally empty — usage persistence arrives in M6.
    }
}
