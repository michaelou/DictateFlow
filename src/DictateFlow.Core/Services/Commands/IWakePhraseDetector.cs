using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The command candidacy of one transcript. <see cref="WakePhraseMatched"/> decides what an
/// unmatched candidate becomes: explicit candidates (wake phrase spoken) produce an
/// unknown-command outcome, implicit ones (wake phrase disabled) fall through to normal
/// dictation.
/// </summary>
/// <param name="CommandText">The verbatim utterance with the wake phrase removed; the whole transcript when the wake phrase is disabled.</param>
/// <param name="WakePhraseMatched">Whether the wake phrase was actually spoken (always <see langword="false"/> when it is disabled).</param>
public sealed record WakePhraseDetection(string CommandText, bool WakePhraseMatched);

/// <summary>
/// Decides — purely on the raw transcript and settings, never via any LLM — whether an
/// utterance is a voice command candidate.
/// </summary>
public interface IWakePhraseDetector
{
    /// <summary>Detects whether <paramref name="transcript"/> is a command candidate.</summary>
    /// <param name="transcript">The raw transcript of the utterance.</param>
    /// <param name="settings">The current voice command settings.</param>
    /// <returns>
    /// The candidate command text, or <see langword="null"/> when the utterance is normal
    /// dictation (wake phrase enabled but not spoken).
    /// </returns>
    WakePhraseDetection? Detect(string transcript, VoiceCommandSettings settings);
}
