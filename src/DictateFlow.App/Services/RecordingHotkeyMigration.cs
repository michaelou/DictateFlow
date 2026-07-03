using System.Text.Json.Nodes;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Migrates the single-hotkey recording schema — <c>Recording.Mode</c> (<c>PushToTalk</c>/<c>Toggle</c>)
/// plus one <c>Recording.Hotkey</c> — to the two independent hotkeys <c>PushToTalkHotkey</c> and
/// <c>ToggleHotkey</c>. The old key is moved into the field matching the old mode and the other is
/// left empty (disabled), so an upgraded install keeps behaving exactly as before. The legacy
/// <c>Mode</c> and <c>Hotkey</c> keys are removed. Idempotent: a <c>Recording</c> section that
/// already carries either new key is left untouched.
/// </summary>
public sealed class RecordingHotkeyMigration : ISettingsMigration
{
    private readonly ILogger<RecordingHotkeyMigration> _logger;

    /// <summary>Initializes a new instance of the <see cref="RecordingHotkeyMigration"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public RecordingHotkeyMigration(ILogger<RecordingHotkeyMigration> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryMigrate(JsonObject settings)
    {
        if (settings.Find("Recording") is not JsonObject recording)
        {
            return false;
        }

        // Already migrated (new shape present) or nothing legacy to move.
        if (recording.Find("PushToTalkHotkey") is not null
            || recording.Find("ToggleHotkey") is not null
            || recording.Find("Hotkey") is null)
        {
            return false;
        }

        var hotkey = recording.Find("Hotkey")?.GetValue<string>() ?? "";
        var isToggle = string.Equals(
            recording.Find("Mode")?.GetValue<string>(),
            RecordingModes.Toggle,
            StringComparison.OrdinalIgnoreCase);

        recording["PushToTalkHotkey"] = isToggle ? "" : hotkey;
        recording["ToggleHotkey"] = isToggle ? hotkey : "";

        Remove(recording, "Mode");
        Remove(recording, "Hotkey");

        _logger.LogInformation(
            "Migrated recording hotkey: '{Hotkey}' → {Target} (other trigger disabled)",
            hotkey,
            isToggle ? "ToggleHotkey" : "PushToTalkHotkey");
        return true;
    }

    /// <summary>Removes a property case-insensitively.</summary>
    private static void Remove(JsonObject obj, string propertyName)
    {
        if (obj.FindKey(propertyName) is { } key)
        {
            obj.Remove(key);
        }
    }
}
