namespace DictateFlow.Core.Models;

/// <summary>
/// The outcome of a speech-to-text request.
/// </summary>
/// <param name="Text">The transcribed text.</param>
/// <param name="AudioDurationSeconds">
/// Duration of the transcribed audio in seconds, used for cost tracking (M6). Providers
/// compute it from the WAV data length when the API does not return it.
/// </param>
/// <param name="Language">The detected or configured language, when known.</param>
public sealed record TranscriptionResult(
    string Text,
    double? AudioDurationSeconds,
    string? Language);
