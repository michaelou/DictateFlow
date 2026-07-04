using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Fail-closed <see cref="ICommandConfirmationService"/> default: denies every command. In
/// effect whenever no UI implementation is registered, so a confirmation-requiring command
/// can never slip through unapproved.
/// </summary>
public sealed class DenyingCommandConfirmationService : ICommandConfirmationService
{
    private readonly ILogger<DenyingCommandConfirmationService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DenyingCommandConfirmationService"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public DenyingCommandConfirmationService(ILogger<DenyingCommandConfirmationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(CommandDefinition command, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Command '{CommandName}' requires confirmation but no confirmation UI is registered; denying",
            command.Name);
        return Task.FromResult(false);
    }
}
