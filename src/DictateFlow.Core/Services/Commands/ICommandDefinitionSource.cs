using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Supplies command definitions to the matcher. Built-in command sets and the user JSON
/// command store (issue #27) each register one of these; the voice command service
/// aggregates all registered sources per utterance, so definition changes apply live.
/// </summary>
public interface ICommandDefinitionSource
{
    /// <summary>Gets the definitions this source currently provides; called per utterance.</summary>
    IReadOnlyList<CommandDefinition> GetDefinitions();
}
