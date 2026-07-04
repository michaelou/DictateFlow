using System.Diagnostics;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Launches an executable named entirely by configuration. <c>ActionValue</c> is the program
/// (an absolute path or a name resolved via <c>PATH</c>, environment variables expanded);
/// <c>ActionArguments</c> is an optional process-arguments template that may embed
/// <c>{{Argument}}</c>, into which the spoken text is substituted as a single escaped argument.
/// </summary>
/// <remarks>
/// The executable is never derived from speech — <c>ActionValue</c> may not contain the
/// placeholder (rejected at load and re-checked here). Arguments are passed to that one program
/// directly; there is no <c>cmd.exe</c>/<c>powershell.exe</c> interpretation, so a spoken
/// argument can never become a second command. Every failure is a curated
/// <see cref="CommandResult.Fail"/>, never a throw.
/// </remarks>
public sealed class ProcessStartAction : ICommandAction, ICommandActionValidator
{
    /// <summary>The action type name this action is registered under.</summary>
    public const string RegistrationName = "ProcessStart";

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<ProcessStartAction> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProcessStartAction"/> class.</summary>
    /// <param name="launcher">Starts the configured process.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ProcessStartAction(IProcessLauncher launcher, ILogger<ProcessStartAction> logger)
    {
        _launcher = launcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Validate(CommandDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ActionValue))
        {
            return "ProcessStart needs an executable path or name in 'value'.";
        }

        if (CommandArgumentPlaceholder.Contains(definition.ActionValue))
        {
            return "ProcessStart 'value' (the executable) must not contain {{Argument}}; "
                + "put the placeholder in 'arguments' instead.";
        }

        return null;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Defense in depth: built-in definitions bypass the store's load-time validation, so the
        // executable-never-from-speech invariant is enforced here too.
        if (CommandArgumentPlaceholder.Contains(context.ActionValue))
        {
            _logger.LogError(
                "Command '{CommandName}' is misconfigured: the executable contains {{Argument}}", context.CommandName);
            return Task.FromResult(CommandResult.Fail(
                $"{context.CommandName} is misconfigured and was not run."));
        }

        if (string.IsNullOrWhiteSpace(context.ActionValue))
        {
            return Task.FromResult(CommandResult.Fail($"{context.CommandName} has no program configured."));
        }

        if (!TryBuildArguments(context, out var arguments, out var failure))
        {
            return Task.FromResult(failure);
        }

        var executable = Environment.ExpandEnvironmentVariables(context.ActionValue.Trim());
        try
        {
            _launcher.Start(new ProcessStartInfo(executable)
            {
                Arguments = arguments,
                UseShellExecute = true,
            });
            _logger.LogInformation("Launched '{Executable}' for command '{CommandName}'", executable, context.CommandName);
            return Task.FromResult(CommandResult.Ok($"Opening {context.CommandName}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not launch '{Executable}' for command '{CommandName}'", executable, context.CommandName);
            return Task.FromResult(CommandResult.Fail(
                $"Couldn't launch {context.CommandName} — '{context.ActionValue}' could not be started."));
        }
    }

    /// <summary>
    /// Builds the final process-arguments string. Environment variables in the template expand;
    /// the spoken argument (when the template asks for it) is escaped as a single argument and is
    /// never itself env-expanded.
    /// </summary>
    private bool TryBuildArguments(CommandContext context, out string arguments, out CommandResult failure)
    {
        failure = CommandResult.Fail("");
        var template = context.ActionArguments ?? "";
        if (!CommandArgumentPlaceholder.Contains(template))
        {
            if (!string.IsNullOrWhiteSpace(context.Argument))
            {
                _logger.LogDebug(
                    "Command '{CommandName}' ignores the spoken argument: no {{Argument}} placeholder", context.CommandName);
            }

            arguments = Environment.ExpandEnvironmentVariables(template);
            return true;
        }

        if (string.IsNullOrWhiteSpace(context.Argument))
        {
            arguments = "";
            failure = CommandResult.Fail(
                "This command needs a spoken argument (e.g. 'open in notepad meeting notes.txt').");
            return false;
        }

        // Expand env vars in the configured template only, then drop the escaped spoken text in —
        // so a `%VAR%` in the user's speech is never expanded.
        var expandedTemplate = Environment.ExpandEnvironmentVariables(template);
        arguments = CommandArgumentPlaceholder.Substitute(
            expandedTemplate, ProcessArgumentEscaper.EscapeInsideQuotes(context.Argument));
        return true;
    }
}
