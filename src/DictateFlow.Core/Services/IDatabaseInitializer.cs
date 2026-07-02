namespace DictateFlow.Core.Services;

/// <summary>
/// Creates the DictateFlow SQLite database and bootstraps its schema on startup.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Opens (creating if necessary) <c>dictateflow.db</c> and runs the idempotent schema
    /// bootstrap. Safe to call multiple times.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending database work.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
