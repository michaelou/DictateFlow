namespace DictateFlow.Core.Services.Updates;

/// <summary>
/// Checks whether a newer DictateFlow release is available. Manual only — this never
/// downloads or installs anything, it just reports what the latest published release is.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Queries the latest published release and compares it with the installed version.
    /// Never throws: offline or network failures come back as
    /// <see cref="UpdateCheckStatus.Failed"/> with a user-facing message.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
