using System.Diagnostics;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Opens a configured folder in Explorer. <c>ActionValue</c> is a folder path that supports
/// environment variables (<c>%USERPROFILE%\Downloads</c>) and the well-known folder names
/// <c>Downloads</c>, <c>Documents</c>, <c>Desktop</c> and <c>Pictures</c>.
/// </summary>
/// <remarks>
/// The folder target stays fully configured — <c>{{Argument}}</c> is not supported here in V1
/// (a spoken path is too error-prone from transcription and widens file-system reach), so a
/// placeholder in the value is rejected at load and re-checked here. The directory must exist
/// before it is opened; otherwise a curated <see cref="CommandResult.Fail"/> is returned.
/// </remarks>
public sealed class OpenFolderAction : ICommandAction, ICommandActionValidator
{
    /// <summary>The action type name this action is registered under.</summary>
    public const string RegistrationName = "OpenFolder";

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<OpenFolderAction> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenFolderAction"/> class.</summary>
    /// <param name="launcher">Opens the folder in Explorer.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OpenFolderAction(IProcessLauncher launcher, ILogger<OpenFolderAction> logger)
    {
        _launcher = launcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Validate(CommandDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ActionValue))
        {
            return "OpenFolder needs a folder path or well-known name in 'value'.";
        }

        if (CommandArgumentPlaceholder.Contains(definition.ActionValue))
        {
            return "OpenFolder does not support {{Argument}}; the folder must be fully configured.";
        }

        return null;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // Defense in depth: built-in definitions bypass load-time validation.
        if (CommandArgumentPlaceholder.Contains(context.ActionValue))
        {
            _logger.LogError(
                "Command '{CommandName}' is misconfigured: OpenFolder does not accept {{Argument}}", context.CommandName);
            return Task.FromResult(CommandResult.Fail($"{context.CommandName} is misconfigured and was not opened."));
        }

        if (!string.IsNullOrWhiteSpace(context.Argument))
        {
            _logger.LogDebug("Command '{CommandName}' ignores the spoken argument: OpenFolder takes none", context.CommandName);
        }

        var path = ResolveFolder(context.ActionValue.Trim());
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            _logger.LogWarning(
                "Command '{CommandName}' target folder does not exist: '{Path}'", context.CommandName, path);
            return Task.FromResult(CommandResult.Fail($"Couldn't open {context.CommandName}: the folder doesn't exist."));
        }

        try
        {
            _launcher.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _logger.LogInformation("Opened folder '{Path}' for command '{CommandName}'", path, context.CommandName);
            return Task.FromResult(CommandResult.Ok($"Opening {context.CommandName}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open folder '{Path}' for command '{CommandName}'", path, context.CommandName);
            return Task.FromResult(CommandResult.Fail($"Couldn't open {context.CommandName}."));
        }
    }

    /// <summary>
    /// Resolves a well-known folder name to its current path, or expands environment variables in
    /// an explicit path. Returns an empty string when a well-known name has no resolved location.
    /// </summary>
    private static string ResolveFolder(string value)
        => value.ToLowerInvariant() switch
        {
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            _ => Environment.ExpandEnvironmentVariables(value),
        };
}
