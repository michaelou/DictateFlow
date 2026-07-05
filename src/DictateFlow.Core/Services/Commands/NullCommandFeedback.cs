using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// No-op <see cref="ICommandFeedback"/> default: produces no overlay state or sound. In effect
/// whenever no UI implementation is registered, so the command pipeline runs unchanged without
/// one (headless tests, and before the app registers its own).
/// </summary>
public sealed class NullCommandFeedback : ICommandFeedback
{
    /// <inheritdoc />
    public void OnCommandRecognized(CommandDefinition command)
    {
    }

    /// <inheritdoc />
    public void OnCommandCompleted(CommandOutcome outcome)
    {
    }
}
