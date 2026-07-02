using System.Buffers.Binary;

namespace DictateFlow.Core.Services.Audio;

/// <summary>
/// Generates in-memory 16 kHz/16-bit/mono WAV streams of silence — the same format the
/// recorder produces. Used by the Settings "Test connection" check, which needs a tiny
/// valid audio payload without touching the microphone.
/// </summary>
public static class SilentWavFactory
{
    private const int SampleRate = 16000;
    private const short BitsPerSample = 16;
    private const short Channels = 1;

    /// <summary>Creates a silent WAV stream of the given duration, positioned at the start.</summary>
    /// <param name="duration">Length of silence to generate.</param>
    /// <returns>A readable, seekable stream containing a complete WAV file.</returns>
    public static MemoryStream Create(TimeSpan duration)
    {
        var sampleCount = (int)(duration.TotalSeconds * SampleRate);
        var dataBytes = sampleCount * (BitsPerSample / 8) * Channels;
        var bytesPerSecond = SampleRate * (BitsPerSample / 8) * Channels;

        var buffer = new byte[44 + dataBytes];
        var span = buffer.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);            // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);             // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], bytesPerSecond);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(BitsPerSample / 8 * Channels)); // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        // The remaining bytes are already zero — PCM silence.

        return new MemoryStream(buffer, writable: false);
    }
}
