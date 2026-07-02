namespace DictateFlow.Core.Models;

/// <summary>
/// Final outcome of a dictation after transcription and LLM enhancement.
/// When enhancement fails the dictation is not lost: <see cref="Text"/> carries the raw
/// transcript and <see cref="EnhancementWarning"/> explains what went wrong.
/// </summary>
/// <param name="Text">The text to present — enhanced, or the raw transcript when enhancement failed.</param>
/// <param name="RawTranscript">The unmodified transcript, kept for debugging and fallback display.</param>
/// <param name="ModeName">The prompt mode that was applied (after any fallback to <c>Raw</c>).</param>
/// <param name="EnhancementWarning">User-presentable warning when enhancement failed; <see langword="null"/> on success.</param>
public sealed record DictationResult(
    string Text,
    string RawTranscript,
    string ModeName,
    string? EnhancementWarning);
