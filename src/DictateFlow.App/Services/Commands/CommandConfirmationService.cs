using System.Windows;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Commands;

/// <summary>
/// App-layer <see cref="ICommandConfirmationService"/> that asks the user to approve a command
/// in a small topmost dialog, replacing the Core fail-closed
/// <c>DenyingCommandConfirmationService</c>. Runs on the UI dispatcher (the voice command
/// pipeline calls it from a background thread) and keeps the fail-closed contract: only an
/// explicit <b>Yes</b> approves; No, Escape, closing the dialog, the timeout
/// (<see cref="VoiceCommandSettings.CommandTimeoutSeconds"/>), and the absence of any UI all deny.
/// </summary>
public sealed class CommandConfirmationService : ICommandConfirmationService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CommandConfirmationService> _logger;

    /// <summary>Initializes a new instance of the <see cref="CommandConfirmationService"/> class.</summary>
    /// <param name="settingsService">Supplies the command timeout used to auto-deny.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CommandConfirmationService(
        ISettingsService settingsService, ILogger<CommandConfirmationService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(CommandDefinition command, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _logger.LogWarning("No UI dispatcher available to confirm command '{CommandName}'; denying", command.Name);
            return false;
        }

        var timeoutSeconds = Math.Max(1, _settingsService.Current.VoiceCommands.CommandTimeoutSeconds);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Show the modal dialog on the UI thread and await its result from the calling thread.
        // Only an explicit Yes (DialogResult == true) approves; anything else denies.
        return await dispatcher.InvokeAsync(() =>
        {
            var window = new CommandConfirmationWindow(command.Name, timeout);
            var approved = window.ShowDialog() == true;
            _logger.LogInformation(
                "Command '{CommandName}' {Decision} at confirmation", command.Name, approved ? "approved" : "denied");
            return approved;
        }).Task.ConfigureAwait(false);
    }
}
