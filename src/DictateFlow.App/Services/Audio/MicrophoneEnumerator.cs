using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// NAudio-based <see cref="IMicrophoneEnumerator"/> using the WaveIn device list, matching
/// the device numbering <see cref="NAudioRecorder"/> records from. The WaveIn API exposes
/// only the product name, so it doubles as the persisted device id.
/// </summary>
public sealed class MicrophoneEnumerator : IMicrophoneEnumerator
{
    private readonly ILogger<MicrophoneEnumerator> _logger;

    /// <summary>Initializes a new instance of the <see cref="MicrophoneEnumerator"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public MicrophoneEnumerator(ILogger<MicrophoneEnumerator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<MicrophoneInfo> GetMicrophones()
    {
        try
        {
            var microphones = new List<MicrophoneInfo>();
            for (var n = 0; n < WaveInEvent.DeviceCount; n++)
            {
                var name = WaveInEvent.GetCapabilities(n).ProductName;
                microphones.Add(new MicrophoneInfo(name, name));
            }

            return microphones;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Microphone enumeration failed");
            return [];
        }
    }
}
