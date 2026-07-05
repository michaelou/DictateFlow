namespace DictateFlow.Core.Models;

/// <summary>
/// States of the on-screen dictation overlay. M2 uses <see cref="Hidden"/> and
/// <see cref="Listening"/>; the remaining states are consumed by later milestones
/// (Processing in M3/M4, Success/Error in M5/M6).
/// </summary>
public enum OverlayState
{
    /// <summary>The overlay is not visible.</summary>
    Hidden,

    /// <summary>Recording is in progress; the overlay shows a live level indicator.</summary>
    Listening,

    /// <summary>Captured audio is being transcribed/enhanced (M3+).</summary>
    Processing,

    /// <summary>The dictation completed successfully (M5+).</summary>
    Success,

    /// <summary>The dictation failed (M5+).</summary>
    Error,

    /// <summary>A recognized voice command is being executed (issue #30).</summary>
    CommandExecuting,

    /// <summary>A voice command executed successfully; shows its outcome message (issue #30).</summary>
    CommandSuccess,

    /// <summary>A voice command failed or the utterance matched no command; shows its outcome message (issue #30).</summary>
    CommandError,
}
