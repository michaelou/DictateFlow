using System.Diagnostics;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Opens a configured URL in the default browser. <c>ActionValue</c> is a URL template that may
/// embed <c>{{Argument}}</c>; the spoken text is URL-encoded before substitution, so it becomes
/// only a query/path value and can never introduce a new scheme, host or path traversal.
/// </summary>
/// <remarks>
/// Both the template (with the placeholder stood in for) and the final URL must be an absolute
/// <c>http</c>/<c>https</c> URL — <c>file:</c>, <c>javascript:</c> and custom schemes are
/// refused, including via a malicious template. Failures are curated
/// <see cref="CommandResult.Fail"/> results, never throws.
/// </remarks>
public sealed class OpenUrlAction : ICommandAction, ICommandActionValidator
{
    /// <summary>The action type name this action is registered under.</summary>
    public const string RegistrationName = "OpenUrl";

    /// <summary>A harmless stand-in for the placeholder used when validating the template's shape.</summary>
    private const string PlaceholderProbe = "x";

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<OpenUrlAction> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenUrlAction"/> class.</summary>
    /// <param name="launcher">Opens the URL with the default browser.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OpenUrlAction(IProcessLauncher launcher, ILogger<OpenUrlAction> logger)
    {
        _launcher = launcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Validate(CommandDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ActionValue))
        {
            return "OpenUrl needs a URL in 'value'.";
        }

        // Validate the template shape by substituting a harmless stand-in for the placeholder.
        return IsAllowedHttpUrl(CommandArgumentPlaceholder.Substitute(definition.ActionValue.Trim(), PlaceholderProbe))
            ? null
            : "OpenUrl 'value' must be an absolute http or https URL.";
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var template = context.ActionValue?.Trim() ?? "";

        // Re-check the template here too: built-in definitions never went through the store's load validation.
        if (!IsAllowedHttpUrl(CommandArgumentPlaceholder.Substitute(template, PlaceholderProbe)))
        {
            _logger.LogError(
                "Command '{CommandName}' has a non-http(s) URL template and was not opened", context.CommandName);
            return Task.FromResult(CommandResult.Fail(
                $"{context.CommandName} is misconfigured: only http and https links can be opened."));
        }

        string finalUrl;
        if (CommandArgumentPlaceholder.Contains(template))
        {
            if (string.IsNullOrWhiteSpace(context.Argument))
            {
                return Task.FromResult(CommandResult.Fail(
                    "This command needs a spoken argument (e.g. 'search for whisper streaming')."));
            }

            finalUrl = CommandArgumentPlaceholder.Substitute(template, Uri.EscapeDataString(context.Argument));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(context.Argument))
            {
                _logger.LogDebug(
                    "Command '{CommandName}' ignores the spoken argument: no {{Argument}} placeholder", context.CommandName);
            }

            finalUrl = template;
        }

        // The final URL is re-validated after substitution: the encoded argument can never have
        // widened the scheme or host, but this makes that guarantee explicit.
        if (!IsAllowedHttpUrl(finalUrl))
        {
            _logger.LogError("Command '{CommandName}' produced a non-http(s) URL and was not opened", context.CommandName);
            return Task.FromResult(CommandResult.Fail($"{context.CommandName} produced an unsafe link and was not opened."));
        }

        try
        {
            _launcher.Start(new ProcessStartInfo(finalUrl) { UseShellExecute = true });
            _logger.LogInformation("Opened URL for command '{CommandName}'", context.CommandName);
            return Task.FromResult(CommandResult.Ok($"Opening {context.CommandName}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open URL for command '{CommandName}'", context.CommandName);
            return Task.FromResult(CommandResult.Fail($"Couldn't open {context.CommandName}."));
        }
    }

    /// <summary>Whether <paramref name="url"/> is an absolute <c>http</c>/<c>https</c> URL.</summary>
    private static bool IsAllowedHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
