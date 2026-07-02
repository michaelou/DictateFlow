using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services;

/// <summary>
/// Receives one <see cref="UsageRecord"/> after each successful billable provider call
/// (speech and LLM). M6 replaces the no-op registration with SQLite persistence and cost math.
/// </summary>
public interface IUsageSink
{
    /// <summary>Records one completed provider call. Must never throw.</summary>
    /// <param name="record">The call that completed.</param>
    void Record(UsageRecord record);
}
