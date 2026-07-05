using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Default <see cref="IVoiceCommandService"/> implementation. Settings and definitions are
/// read per utterance, so changes apply live. Execution is restricted to actions resolved
/// through the <see cref="ICommandActionResolver"/> allowlist; a matched command whose action
/// type is unknown fails without executing anything, and an utterance that carries the wake
/// phrase but matches no command produces an unknown outcome — never an execution.
/// </summary>
public sealed class VoiceCommandService : IVoiceCommandService
{
    private readonly ISettingsService _settingsService;
    private readonly IWakePhraseDetector _wakePhraseDetector;
    private readonly ICommandMatcher _matcher;
    private readonly IEnumerable<ICommandDefinitionSource> _definitionSources;
    private readonly ICommandActionResolver _actionResolver;
    private readonly ICommandConfirmationService _confirmationService;
    private readonly ICommandFeedback _feedback;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VoiceCommandService> _logger;

    /// <summary>Initializes a new instance of the <see cref="VoiceCommandService"/> class.</summary>
    /// <param name="settingsService">Supplies the voice command settings, read per utterance.</param>
    /// <param name="wakePhraseDetector">Decides whether an utterance is a command candidate.</param>
    /// <param name="matcher">Matches the command text against the configured phrases.</param>
    /// <param name="definitionSources">All registered definition sources; aggregated per utterance.</param>
    /// <param name="actionResolver">Resolves matched action types from the registered allowlist.</param>
    /// <param name="confirmationService">Approves confirmation-requiring commands; denies by default.</param>
    /// <param name="feedback">Surfaces recognition and completion to the user (overlay/sounds); no-op by default.</param>
    /// <param name="timeProvider">Supplies the execution timestamp (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public VoiceCommandService(
        ISettingsService settingsService,
        IWakePhraseDetector wakePhraseDetector,
        ICommandMatcher matcher,
        IEnumerable<ICommandDefinitionSource> definitionSources,
        ICommandActionResolver actionResolver,
        ICommandConfirmationService confirmationService,
        ICommandFeedback feedback,
        TimeProvider timeProvider,
        ILogger<VoiceCommandService> logger)
    {
        _settingsService = settingsService;
        _wakePhraseDetector = wakePhraseDetector;
        _matcher = matcher;
        _definitionSources = definitionSources;
        _actionResolver = actionResolver;
        _confirmationService = confirmationService;
        _feedback = feedback;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommandOutcome?> TryHandleAsync(string transcript, CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.VoiceCommands;
        if (!settings.Enabled)
        {
            return null;
        }

        var detection = _wakePhraseDetector.Detect(transcript, settings);
        if (detection is null)
        {
            return null;
        }

        var match = _matcher.Match(detection.CommandText, CollectDefinitions());
        if (match is null)
        {
            if (!detection.WakePhraseMatched)
            {
                // No wake phrase in play: an unmatched utterance is just dictation.
                return null;
            }

            _logger.LogInformation("Wake phrase spoken but no command matched: '{CommandText}'", detection.CommandText);
            return Completed(new CommandOutcome(
                CommandOutcomeStatus.Unknown, null,
                detection.CommandText.Length == 0
                    ? "No command was spoken after the wake phrase."
                    : $"Unknown voice command: “{detection.CommandText}”. Nothing was executed."));
        }

        var definition = match.Definition;

        // A command was recognized: surface it (overlay/sound) before confirmation and execution.
        Notify(() => _feedback.OnCommandRecognized(definition));

        if (definition.RequiresConfirmation || settings.RequireConfirmation)
        {
            if (!await ConfirmAsync(definition, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Command '{CommandName}' declined at confirmation", definition.Name);
                return Completed(new CommandOutcome(
                    CommandOutcomeStatus.Declined, definition.Name, $"{definition.Name} was not executed."));
            }
        }

        if (!_actionResolver.TryResolve(definition.ActionType, out var action))
        {
            // The allowlist boundary: a definition naming an unregistered action type never executes.
            _logger.LogError(
                "Command '{CommandName}' uses unknown action type '{ActionType}'; valid types: {ValidTypes}",
                definition.Name, definition.ActionType, string.Join(", ", _actionResolver.GetActionTypes()));
            return Completed(new CommandOutcome(
                CommandOutcomeStatus.Failed, definition.Name,
                $"{definition.Name} uses the unknown action type '{definition.ActionType}' and was not executed."));
        }

        var outcome = await ExecuteAsync(action, definition, match.Argument, transcript, settings, cancellationToken)
            .ConfigureAwait(false);
        return Completed(outcome);
    }

    /// <summary>Reports a terminal outcome to the feedback sink (guarded) and returns it unchanged.</summary>
    private CommandOutcome Completed(CommandOutcome outcome)
    {
        Notify(() => _feedback.OnCommandCompleted(outcome));
        return outcome;
    }

    /// <summary>Invokes a feedback callback, swallowing failures — presentation must never break command handling.</summary>
    private void Notify(Action feedback)
    {
        try
        {
            feedback();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command feedback failed; ignoring");
        }
    }

    /// <summary>Aggregates the definitions of all sources; a failing source is logged and skipped.</summary>
    private List<CommandDefinition> CollectDefinitions()
    {
        var definitions = new List<CommandDefinition>();
        foreach (var source in _definitionSources)
        {
            try
            {
                definitions.AddRange(source.GetDefinitions());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command definition source {SourceType} failed; skipping it", source.GetType().Name);
            }
        }

        return definitions;
    }

    /// <summary>Asks for confirmation, failing closed: any error while asking counts as a denial.</summary>
    private async Task<bool> ConfirmAsync(CommandDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            return await _confirmationService.ConfirmAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmation for command '{CommandName}' failed; denying", definition.Name);
            return false;
        }
    }

    /// <summary>Runs the action under the command timeout; every failure becomes a curated outcome.</summary>
    private async Task<CommandOutcome> ExecuteAsync(
        ICommandAction action,
        CommandDefinition definition,
        string argument,
        string transcript,
        VoiceCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var context = new CommandContext(
            definition.Name, definition.ActionType, definition.ActionValue, definition.ActionArguments,
            argument, transcript, _timeProvider.GetUtcNow().UtcDateTime);
        var timeoutSeconds = Math.Max(1, settings.CommandTimeoutSeconds);
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var result = await action.ExecuteAsync(context, timeoutSource.Token).ConfigureAwait(false);
            _logger.LogInformation(
                "Command '{CommandName}' ({ActionType}) finished: {Success} — {Message}",
                definition.Name, definition.ActionType, result.Success, result.Message);
            return new CommandOutcome(
                result.Success ? CommandOutcomeStatus.Executed : CommandOutcomeStatus.Failed,
                definition.Name, result.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Command '{CommandName}' timed out after {TimeoutSeconds} s", definition.Name, timeoutSeconds);
            return new CommandOutcome(
                CommandOutcomeStatus.Failed, definition.Name,
                $"{definition.Name} timed out after {timeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Command '{CommandName}' cancelled", definition.Name);
            return new CommandOutcome(
                CommandOutcomeStatus.Failed, definition.Name, $"{definition.Name} was cancelled.");
        }
        catch (Exception ex)
        {
            // Raw exception text never reaches the user; a generic message stands in.
            _logger.LogError(ex, "Command '{CommandName}' ({ActionType}) failed", definition.Name, definition.ActionType);
            return new CommandOutcome(
                CommandOutcomeStatus.Failed, definition.Name,
                $"{definition.Name} failed — see the log for details.");
        }
    }
}
