using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Default <see cref="IWakePhraseDetector"/> implementation. Matching is token-based
/// (case-insensitive, punctuation ignored), so <c>"Hey John, open Notepad."</c> matches the
/// wake phrase <c>Hey John</c>; the command text keeps the remainder verbatim. A blank wake
/// phrase with the wake phrase enabled never matches — commands are then unreachable rather
/// than every utterance becoming one (settings validation flags this).
/// </summary>
public sealed class WakePhraseDetector : IWakePhraseDetector
{
    private readonly ILogger<WakePhraseDetector> _logger;

    /// <summary>Initializes a new instance of the <see cref="WakePhraseDetector"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public WakePhraseDetector(ILogger<WakePhraseDetector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public WakePhraseDetection? Detect(string transcript, VoiceCommandSettings settings)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        if (!settings.WakePhraseEnabled)
        {
            // No wake phrase: every utterance is a candidate, but an unmatched one falls
            // through to dictation (WakePhraseMatched stays false).
            return new WakePhraseDetection(transcript.Trim(), WakePhraseMatched: false);
        }

        var wakeTokens = CommandTokenizer.Tokenize(settings.WakePhrase ?? "");
        if (wakeTokens.Count == 0)
        {
            _logger.LogWarning("Wake phrase is enabled but empty; voice commands are unreachable");
            return null;
        }

        var transcriptTokens = CommandTokenizer.Tokenize(transcript);
        if (!CommandTokenizer.StartsWith(transcriptTokens, wakeTokens))
        {
            return null;
        }

        var commandText = CommandTokenizer.Remainder(transcript, transcriptTokens, wakeTokens.Count);
        _logger.LogDebug("Wake phrase detected; command text: '{CommandText}'", commandText);
        return new WakePhraseDetection(commandText, WakePhraseMatched: true);
    }
}
