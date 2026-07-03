namespace DictateFlow.Core.Models;

/// <summary>
/// One update from a streaming transcription: the transcript as recognized so far.
/// </summary>
/// <param name="Text">
/// The full transcript recognized so far — each update replaces the previous one; updates
/// are never deltas.
/// </param>
/// <param name="IsFinal">
/// Whether this is the provider's final transcript. At most one final update is produced,
/// as the last element of the stream.
/// </param>
public sealed record TranscriptionUpdate(string Text, bool IsFinal);
