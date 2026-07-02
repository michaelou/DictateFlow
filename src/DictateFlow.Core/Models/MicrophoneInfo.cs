namespace DictateFlow.Core.Models;

/// <summary>Describes an audio capture (microphone) device.</summary>
/// <param name="DeviceId">Stable identifier persisted in <see cref="RecordingSettings.MicrophoneDeviceId"/>.</param>
/// <param name="Name">Human-readable device name shown in the settings UI.</param>
public sealed record MicrophoneInfo(string DeviceId, string Name);
