using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Fake <see cref="ICommandAction"/> that only logs and reports success, so the whole voice
/// command flow — wake phrase, matching, confirmation, feedback — is demoable and testable
/// before any real action type exists, mirroring the mock providers.
/// </summary>
public sealed class MockCommandAction : ICommandAction
{
    /// <summary>The action type name this action is registered under.</summary>
    public const string RegistrationName = "Mock";

    private readonly ILogger<MockCommandAction> _logger;

    /// <summary>Initializes a new instance of the <see cref="MockCommandAction"/> class.</summary>
    /// <param name="logger">Receives the executed-command log line.</param>
    public MockCommandAction(ILogger<MockCommandAction> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Mock command action executed for '{CommandName}' (argument: '{Argument}')",
            context.CommandName, context.Argument);
        return Task.FromResult(CommandResult.Ok($"{context.CommandName} executed."));
    }
}

/// <summary>
/// Built-in definitions for <see cref="MockCommandAction"/>: <c>test command</c> gives users
/// (and tests) an end-to-end voice command without configuring anything.
/// </summary>
public sealed class MockCommandDefinitionSource : ICommandDefinitionSource
{
    private static readonly IReadOnlyList<CommandDefinition> Definitions =
    [
        new CommandDefinition
        {
            Name = "Test Command",
            Phrases = ["test command", "run a test command"],
            ActionType = MockCommandAction.RegistrationName,
        },
    ];

    /// <inheritdoc />
    public IReadOnlyList<CommandDefinition> GetDefinitions() => Definitions;
}
