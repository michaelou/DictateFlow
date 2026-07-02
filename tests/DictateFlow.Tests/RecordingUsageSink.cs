using DictateFlow.Core.Models;
using DictateFlow.Core.Services;

namespace DictateFlow.Tests;

/// <summary><see cref="IUsageSink"/> test double that keeps every record for assertions.</summary>
public sealed class RecordingUsageSink : IUsageSink
{
    /// <summary>Gets the records received so far, in order.</summary>
    public List<UsageRecord> Records { get; } = [];

    /// <inheritdoc />
    public void Record(UsageRecord record) => Records.Add(record);
}
