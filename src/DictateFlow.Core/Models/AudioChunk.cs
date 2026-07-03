namespace DictateFlow.Core.Models;

/// <summary>
/// One buffer of live audio captured while recording is in progress, used to feed streaming
/// transcription. The samples are raw 16 kHz / 16-bit / mono PCM without a WAV header.
/// </summary>
/// <param name="Pcm">The PCM samples; the array is owned by the chunk and never reused.</param>
public sealed record AudioChunk(ReadOnlyMemory<byte> Pcm);
