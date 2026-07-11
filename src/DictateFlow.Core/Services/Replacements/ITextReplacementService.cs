using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Replacements;

/// <summary>
/// Applies the replacement dictionary (issue #35) to a transcript, rewriting misheard names,
/// acronyms and jargon to their intended spelling. The rules come from
/// <see cref="AppSettings.Replacements"/>, read per call so edits apply live.
/// </summary>
public interface ITextReplacementService
{
    /// <summary>
    /// Rewrites <paramref name="text"/> by applying every configured replacement rule in order.
    /// Returns the text unchanged when no rule matches (or none are configured).
    /// </summary>
    /// <param name="text">The transcript to correct.</param>
    /// <returns>The corrected text.</returns>
    string Apply(string text);
}
