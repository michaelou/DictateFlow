using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;

namespace DictateFlow.App.Services.Commands;

/// <summary>
/// The built-in DictateFlow commands shipped as code (not user JSON), so the app's own
/// operations — Settings, History, the Cost Dashboard, the update check and prompt-mode
/// switching — work out of the box. Every command uses <see cref="DictateFlowAction"/> with a
/// fixed <see cref="DictateFlowOperation"/> value.
/// </summary>
public sealed class DictateFlowCommandDefinitionSource : ICommandDefinitionSource
{
    private static readonly IReadOnlyList<CommandDefinition> Definitions =
    [
        new CommandDefinition
        {
            Name = "Open Settings",
            Phrases = ["open settings", "open the settings"],
            ActionType = DictateFlowAction.RegistrationName,
            ActionValue = nameof(DictateFlowOperation.OpenSettings),
        },
        new CommandDefinition
        {
            Name = "Show History",
            Phrases = ["show history", "open history", "show my history"],
            ActionType = DictateFlowAction.RegistrationName,
            ActionValue = nameof(DictateFlowOperation.ShowHistory),
        },
        new CommandDefinition
        {
            Name = "Open Cost Dashboard",
            Phrases = ["open cost dashboard", "show cost dashboard", "open the cost dashboard"],
            ActionType = DictateFlowAction.RegistrationName,
            ActionValue = nameof(DictateFlowOperation.OpenCostDashboard),
        },
        new CommandDefinition
        {
            Name = "Check for Updates",
            Phrases = ["check for updates", "check for update"],
            ActionType = DictateFlowAction.RegistrationName,
            ActionValue = nameof(DictateFlowOperation.CheckForUpdates),
        },
        new CommandDefinition
        {
            // Prefix command: the utterance remainder after "switch to" is the mode name
            // (e.g. "switch to Email mode" → argument "Email mode").
            Name = "Switch Prompt Mode",
            Phrases = ["switch to", "switch mode to", "change to"],
            ActionType = DictateFlowAction.RegistrationName,
            ActionValue = nameof(DictateFlowOperation.SwitchPromptMode),
        },
    ];

    /// <inheritdoc />
    public IReadOnlyList<CommandDefinition> GetDefinitions() => Definitions;
}
