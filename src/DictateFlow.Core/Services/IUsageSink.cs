using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services;

/// <summary>
/// Receives one <see cref="UsageRecord"/> after each successful billable provider call
/// (speech and LLM), persisting it with an estimated cost for the cost dashboard.
/// </summary>
public interface IUsageSink
{
    /// <summary>Records one completed provider call. Must never throw.</summary>
    /// <param name="record">The call that completed.</param>
    void Record(UsageRecord record);
}
