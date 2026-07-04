using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Asks the user to approve one command before it executes. The App layer implements this
/// with a dialog (issue #30); Core ships <see cref="DenyingCommandConfirmationService"/> as
/// the fail-closed default — without a confirmation UI, confirmation-requiring commands
/// never execute.
/// </summary>
public interface ICommandConfirmationService
{
    /// <summary>Asks the user to approve <paramref name="command"/>.</summary>
    /// <param name="command">The command about to execute.</param>
    /// <param name="cancellationToken">Cancels the wait for an answer.</param>
    /// <returns><see langword="true"/> only on an explicit approval; anything else denies.</returns>
    Task<bool> ConfirmAsync(CommandDefinition command, CancellationToken cancellationToken);
}
