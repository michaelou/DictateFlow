using System.Text;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// One word of an utterance or phrase, prepared for matching.
/// </summary>
/// <param name="Normalized">The word lowercased with punctuation stripped (letters and digits only).</param>
/// <param name="StartIndex">Index of the word's first character in the original text, so the verbatim remainder can be sliced out.</param>
public readonly record struct CommandToken(string Normalized, int StartIndex);

/// <summary>
/// Shared tokenization for wake-phrase detection and command matching: split on whitespace,
/// strip punctuation, lowercase. Tokens that are pure punctuation (a stray <c>—</c> from
/// transcription) vanish, which is what makes <c>"Hey John, open Notepad."</c> equal
/// <c>hey john open notepad</c>. Start indexes point into the original text so arguments are
/// extracted verbatim, punctuation and all.
/// </summary>
public static class CommandTokenizer
{
    /// <summary>Tokenizes <paramref name="text"/>; tokens that normalize to nothing are dropped.</summary>
    /// <param name="text">The utterance or phrase to tokenize.</param>
    public static IReadOnlyList<CommandToken> Tokenize(string text)
    {
        var tokens = new List<CommandToken>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var start = index;
            var builder = new StringBuilder();
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                var character = text[index];
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }

                index++;
            }

            if (builder.Length > 0)
            {
                tokens.Add(new CommandToken(builder.ToString(), start));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Whether the first tokens of <paramref name="text"/> are exactly
    /// <paramref name="prefix"/>. Empty prefixes never match.
    /// </summary>
    /// <param name="text">The utterance tokens.</param>
    /// <param name="prefix">The phrase tokens that must appear first.</param>
    public static bool StartsWith(IReadOnlyList<CommandToken> text, IReadOnlyList<CommandToken> prefix)
    {
        if (prefix.Count == 0 || prefix.Count > text.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (text[i].Normalized != prefix[i].Normalized)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The verbatim remainder of <paramref name="text"/> after its first
    /// <paramref name="consumedTokens"/> tokens — the command text after a wake phrase, or
    /// the argument after a matched phrase. Empty when everything was consumed.
    /// </summary>
    /// <param name="text">The original text the tokens came from.</param>
    /// <param name="tokens">The tokens of <paramref name="text"/>.</param>
    /// <param name="consumedTokens">How many leading tokens were matched.</param>
    public static string Remainder(string text, IReadOnlyList<CommandToken> tokens, int consumedTokens)
        => consumedTokens >= tokens.Count ? "" : text[tokens[consumedTokens].StartIndex..].Trim();
}
