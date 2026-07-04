namespace DictateFlow.Core.Models;

/// <summary>
/// Outcome of a dictation pipeline run. Three shapes occur:
/// success (<see cref="Success"/> with a non-null <see cref="FinalText"/>), user cancellation
/// from the output gate (<see cref="Success"/> with a <see langword="null"/>
/// <see cref="FinalText"/> — not an error), and failure (<see cref="Success"/> is
/// <see langword="false"/> and <see cref="ErrorMessage"/> is user-presentable).
/// The same record also carries the draft offered to <c>IOutputGate</c>, where
/// <see cref="ErrorMessage"/> holds the enhancement-fallback warning, if any.
/// A fourth shape exists since issue #26: the utterance was a voice command —
/// <see cref="Command"/> is non-null, no text was pasted, and success mirrors whether the
/// command executed.
/// </summary>
/// <param name="Success">Whether the run completed without a failure (a user cancel still counts as success).</param>
/// <param name="FinalText">The text that was (or would be) delivered; <see langword="null"/> after a cancel or an early failure.</param>
/// <param name="RawTranscript">The unmodified transcript, when transcription succeeded.</param>
/// <param name="ErrorMessage">User-presentable failure description (or fallback warning on a gate draft); <see langword="null"/> otherwise.</param>
/// <param name="IsConfigurationError">Whether the failure is configuration-caused — the UI should offer a shortcut to Settings.</param>
/// <param name="Command">The voice command outcome when the utterance was handled as a command; <see langword="null"/> on a normal dictation.</param>
public sealed record PipelineResult(
    bool Success, string? FinalText, string? RawTranscript, string? ErrorMessage, bool IsConfigurationError = false,
    CommandOutcome? Command = null);
