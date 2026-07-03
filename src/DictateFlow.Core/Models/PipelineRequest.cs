namespace DictateFlow.Core.Models;

/// <summary>
/// Input to a dictation pipeline run: the completed audio capture plus the context of the
/// application the user was dictating into, taken when recording started.
/// </summary>
/// <param name="Audio">The recorded WAV audio; the pipeline rewinds seekable streams before transcribing.</param>
/// <param name="ApplicationName">Process name of the foreground application at record-start (empty when unknown).</param>
/// <param name="TargetWindowHandle">Native handle of the foreground window at record-start, used to re-focus it before output; <c>0</c> when unknown.</param>
/// <param name="Transcript">
/// Transcript already produced by streaming transcription while recording, or
/// <see langword="null"/> for the standard workflow. When set, the pipeline skips its
/// transcription stage and uses this text directly.
/// </param>
public sealed record PipelineRequest(
    Stream Audio, string ApplicationName, nint TargetWindowHandle, string? Transcript = null);
