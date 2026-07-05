using DictateFlow.App.Services;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Commands;
using DictateFlow.Core.Services.Prompts;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Commands;

/// <summary>
/// Runs one of DictateFlow's own internal operations by voice. <c>ActionValue</c> names a member
/// of the fixed <see cref="DictateFlowOperation"/> enum — never a program, script or path — so
/// the action can only ever reach a built-in window/update/mode operation. Unknown values are
/// rejected at load (<see cref="Validate"/>) and re-checked here, and every failure is a curated
/// <see cref="CommandResult.Fail"/> rather than a throw.
/// </summary>
/// <remarks>
/// The window and update operations delegate to <see cref="IAppActions"/> — the same code the
/// tray menu runs. <see cref="DictateFlowOperation.SwitchPromptMode"/> is the reference example
/// of an action consuming the spoken argument directly (no <c>{{Argument}}</c> placeholder): the
/// utterance remainder after <c>switch to</c> is resolved against the loaded prompt modes.
/// </remarks>
public sealed class DictateFlowAction : ICommandAction, ICommandActionValidator
{
    /// <summary>The action type name this action is registered under.</summary>
    public const string RegistrationName = "DictateFlowAction";

    private readonly IAppActions _appActions;
    private readonly ISettingsService _settingsService;
    private readonly IPromptModeStore _promptModeStore;
    private readonly ILogger<DictateFlowAction> _logger;

    /// <summary>Initializes a new instance of the <see cref="DictateFlowAction"/> class.</summary>
    /// <param name="appActions">Runs the window/update operations (shared with the tray menu).</param>
    /// <param name="settingsService">Reads and persists the active prompt mode.</param>
    /// <param name="promptModeStore">Supplies the loaded prompt modes the spoken argument resolves against.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictateFlowAction(
        IAppActions appActions,
        ISettingsService settingsService,
        IPromptModeStore promptModeStore,
        ILogger<DictateFlowAction> logger)
    {
        _appActions = appActions;
        _settingsService = settingsService;
        _promptModeStore = promptModeStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Validate(CommandDefinition definition)
    {
        if (!TryParseOperation(definition.ActionValue, out _))
        {
            return $"DictateFlowAction 'value' must be one of: {string.Join(", ", Enum.GetNames<DictateFlowOperation>())}.";
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Defense in depth: built-in definitions bypass the store's load-time validation.
        if (!TryParseOperation(context.ActionValue, out var operation))
        {
            _logger.LogError(
                "Command '{CommandName}' names the unknown DictateFlow operation '{Operation}'",
                context.CommandName, context.ActionValue);
            return CommandResult.Fail($"{context.CommandName} is misconfigured and was not run.");
        }

        switch (operation)
        {
            case DictateFlowOperation.OpenSettings:
                _appActions.OpenSettings();
                return CommandResult.Ok("Opening Settings.");

            case DictateFlowOperation.ShowHistory:
                _appActions.ShowHistory();
                return CommandResult.Ok("Opening History.");

            case DictateFlowOperation.OpenCostDashboard:
                _appActions.OpenCostDashboard();
                return CommandResult.Ok("Opening the Cost Dashboard.");

            case DictateFlowOperation.CheckForUpdates:
                await _appActions.CheckForUpdatesAsync().ConfigureAwait(false);
                return CommandResult.Ok("Checking for updates.");

            case DictateFlowOperation.SwitchPromptMode:
                return await SwitchPromptModeAsync(context.Argument).ConfigureAwait(false);

            default:
                return CommandResult.Fail($"{context.CommandName} is not supported.");
        }
    }

    /// <summary>Resolves the spoken argument to a loaded mode and makes it active, or fails without changing anything.</summary>
    private async Task<CommandResult> SwitchPromptModeAsync(string argument)
    {
        var modes = _promptModeStore.GetAll();
        if (!TryResolvePromptMode(argument, modes, out var mode))
        {
            _logger.LogInformation("No prompt mode matches the spoken argument '{Argument}'", argument);
            return CommandResult.Fail(
                string.IsNullOrWhiteSpace(argument)
                    ? "Say which mode to switch to, e.g. “switch to Email mode”."
                    : $"No prompt mode matches “{argument.Trim()}”.");
        }

        if (string.Equals(_settingsService.Current.ActivePromptMode, mode.Name, StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Ok($"Already in {mode.Name} mode.");
        }

        _logger.LogInformation("Active prompt mode changed to '{PromptMode}' by voice command", mode.Name);
        _settingsService.Current.ActivePromptMode = mode.Name;
        await _settingsService.SaveAsync().ConfigureAwait(false);
        return CommandResult.Ok($"Switched to {mode.Name} mode.");
    }

    /// <summary>
    /// Resolves a spoken mode name against the loaded modes: case-insensitive, tolerating a
    /// trailing <c>mode</c> (e.g. <c>Email mode</c> → <c>Email</c>). An exact (with the suffix)
    /// match wins first, so a mode literally named <c>… mode</c> still resolves.
    /// </summary>
    /// <param name="argument">The spoken utterance remainder after the matched phrase.</param>
    /// <param name="modes">The loaded prompt modes to resolve against.</param>
    /// <param name="mode">The resolved mode when found.</param>
    /// <returns><see langword="true"/> when a mode matched.</returns>
    public static bool TryResolvePromptMode(
        string argument, IReadOnlyList<PromptMode> modes, out PromptMode mode)
    {
        var name = (argument ?? "").Trim();
        if (name.Length > 0)
        {
            if (FindMode(name, modes) is { } exact)
            {
                mode = exact;
                return true;
            }

            if (name.EndsWith(" mode", StringComparison.OrdinalIgnoreCase)
                && FindMode(name[..^" mode".Length].Trim(), modes) is { } stripped)
            {
                mode = stripped;
                return true;
            }
        }

        mode = null!;
        return false;
    }

    /// <summary>Finds a loaded mode by exact case-insensitive name, or <see langword="null"/>.</summary>
    private static PromptMode? FindMode(string name, IReadOnlyList<PromptMode> modes)
        => modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses an action value to a <see cref="DictateFlowOperation"/> (case-insensitive), ignoring blanks.</summary>
    private static bool TryParseOperation(string? value, out DictateFlowOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out operation)
            && Enum.IsDefined(operation))
        {
            return true;
        }

        operation = default;
        return false;
    }
}
