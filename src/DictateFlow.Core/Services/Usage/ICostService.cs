using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Usage;

/// <summary>
/// Aggregates the persisted <c>UsageRecords</c> rows into the Today / This month / Lifetime
/// figures shown by the cost dashboard.
/// </summary>
public interface ICostService
{
    /// <summary>
    /// Computes the current cost summary. Day and month boundaries are evaluated in local
    /// time (timestamps are stored in UTC).
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending database I/O.</param>
    Task<CostSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
