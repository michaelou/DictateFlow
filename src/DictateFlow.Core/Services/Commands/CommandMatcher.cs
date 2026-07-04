using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Default <see cref="ICommandMatcher"/> implementation. A phrase matches when its tokens
/// equal the leading tokens of the command text; consuming every token is an exact match,
/// anything left over becomes the argument (<c>"remind me in 10 minutes …"</c> matches the
/// phrase <c>remind me</c> with argument <c>in 10 minutes …</c>). Exact matches beat prefix
/// matches, longer phrases beat shorter ones, and a remaining tie keeps the first definition
/// in source order — logged, so ambiguous configurations are visible.
/// </summary>
public sealed class CommandMatcher : ICommandMatcher
{
    private readonly ILogger<CommandMatcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="CommandMatcher"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public CommandMatcher(ILogger<CommandMatcher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CommandMatch? Match(string commandText, IReadOnlyList<CommandDefinition> definitions)
    {
        var textTokens = CommandTokenizer.Tokenize(commandText);
        if (textTokens.Count == 0)
        {
            return null;
        }

        // Collect every phrase hit, then pick deterministically: exact beats prefix, longer
        // beats shorter, first in source order breaks the remaining tie.
        var candidates = new List<(CommandDefinition Definition, string Phrase, int TokenCount, bool Exact)>();
        foreach (var definition in definitions)
        {
            if (!definition.Enabled)
            {
                continue;
            }

            foreach (var phrase in definition.Phrases)
            {
                var phraseTokens = CommandTokenizer.Tokenize(phrase ?? "");
                if (CommandTokenizer.StartsWith(textTokens, phraseTokens))
                {
                    candidates.Add((definition, phrase!, phraseTokens.Count, phraseTokens.Count == textTokens.Count));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(c => c.Exact)
            .ThenByDescending(c => c.TokenCount)
            .First();

        var rivals = candidates.Where(
            c => c.Definition != best.Definition && c.Exact == best.Exact && c.TokenCount == best.TokenCount).ToList();
        if (rivals.Count > 0)
        {
            _logger.LogWarning(
                "Ambiguous command phrases for '{CommandText}': '{Winner}' wins over {Rivals}",
                commandText, best.Definition.Name, string.Join(", ", rivals.Select(r => $"'{r.Definition.Name}'")));
        }

        var argument = CommandTokenizer.Remainder(commandText, textTokens, best.TokenCount);
        _logger.LogDebug(
            "Matched command '{CommandName}' via phrase '{Phrase}' (argument: '{Argument}')",
            best.Definition.Name, best.Phrase, argument);
        return new CommandMatch(best.Definition, best.Phrase, argument);
    }
}
