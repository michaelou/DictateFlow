using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// One successful match: the command, the phrase that hit, and the spoken argument.
/// </summary>
/// <param name="Definition">The matched command definition.</param>
/// <param name="Phrase">The phrase that matched, as configured.</param>
/// <param name="Argument">The verbatim utterance remainder after the phrase; empty on an exact match.</param>
public sealed record CommandMatch(CommandDefinition Definition, string Phrase, string Argument);

/// <summary>
/// Matches command text (the utterance after the wake phrase) against configured command
/// phrases. Deterministic by design: exact and prefix token matching only, case-insensitive,
/// punctuation ignored — no fuzzy or AI-assisted matching in V1.
/// </summary>
public interface ICommandMatcher
{
    /// <summary>Finds the best-matching enabled command, or <see langword="null"/> when nothing matches.</summary>
    /// <param name="commandText">The utterance with the wake phrase already removed.</param>
    /// <param name="definitions">The definitions to match against, in source order.</param>
    CommandMatch? Match(string commandText, IReadOnlyList<CommandDefinition> definitions);
}
