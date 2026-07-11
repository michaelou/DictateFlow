namespace DictateFlow.Core.Models;

/// <summary>
/// One entry of the replacement dictionary (issue #35): every occurrence of <see cref="From"/>
/// in a transcript is rewritten to <see cref="To"/>. Fixes speech-to-text mishearings of names,
/// acronyms and jargon that no language model would otherwise correct (e.g. <c>Marco</c> → <c>Marko</c>).
/// Applied deterministically to the transcript before LLM enhancement, so corrections stick
/// whether or not enhancement runs; the pairs are also offered to prompts via the
/// <c>{{ReplacementDictionary}}</c> variable so a rewrite cannot undo them.
/// </summary>
public sealed class ReplacementRule
{
    /// <summary>Gets or sets the text to search for (the misheard word or phrase).</summary>
    public string From { get; set; } = "";

    /// <summary>Gets or sets the text each occurrence of <see cref="From"/> is replaced with.</summary>
    public string To { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="From"/> matches only whole words
    /// (bounded by non-word characters). On by default so replacing <c>Marco</c> leaves
    /// <c>Marconi</c> untouched; turn it off to substitute inside words or across spaces.
    /// </summary>
    public bool WholeWord { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the match is case-sensitive. Off by default so a
    /// mishearing is corrected regardless of how the speech engine capitalized it.
    /// </summary>
    public bool CaseSensitive { get; set; }
}
