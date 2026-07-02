namespace DictateFlow.Core.Models;

/// <summary>Well-known values for <see cref="RecordingSettings.Mode"/>.</summary>
public static class RecordingModes
{
    /// <summary>Recording runs while the hotkey chord is held down.</summary>
    public const string PushToTalk = "PushToTalk";

    /// <summary>The hotkey starts recording; pressing it again stops.</summary>
    public const string Toggle = "Toggle";
}
