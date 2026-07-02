using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Audio;

/// <summary>Enumerates the audio capture devices available on the machine.</summary>
public interface IMicrophoneEnumerator
{
    /// <summary>Returns the microphones currently present; empty when none (or on enumeration failure).</summary>
    /// <returns>The available capture devices.</returns>
    IReadOnlyList<MicrophoneInfo> GetMicrophones();
}
