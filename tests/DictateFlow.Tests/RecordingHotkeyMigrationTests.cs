using System.Text.Json.Nodes;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="RecordingHotkeyMigration"/> (single <c>Mode</c>+<c>Hotkey</c> →
/// independent <c>PushToTalkHotkey</c>/<c>ToggleHotkey</c>) and its integration into
/// <see cref="SettingsService.LoadAsync"/>.
/// </summary>
public sealed class RecordingHotkeyMigrationTests
{
    private static RecordingHotkeyMigration CreateMigration()
        => new(NullLogger<RecordingHotkeyMigration>.Instance);

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void TryMigrate_ToggleMode_MovesHotkeyToToggleAndClearsPushToTalk()
    {
        var root = Parse("""{ "Recording": { "Mode": "Toggle", "Hotkey": "Ctrl+Shift+D", "SilenceTimeoutSeconds": 45 } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        var recording = root["Recording"]!;
        Assert.Equal("Ctrl+Shift+D", (string?)recording["ToggleHotkey"]);
        Assert.Equal("", (string?)recording["PushToTalkHotkey"]);
        Assert.Null(recording["Mode"]);
        Assert.Null(recording["Hotkey"]);
        Assert.Equal(45, (int?)recording["SilenceTimeoutSeconds"]); // untouched
    }

    [Fact]
    public void TryMigrate_PushToTalkMode_MovesHotkeyToPushToTalkAndClearsToggle()
    {
        var root = Parse("""{ "Recording": { "Mode": "PushToTalk", "Hotkey": "Ctrl+Alt+D" } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        Assert.Equal("Ctrl+Alt+D", (string?)root["Recording"]!["PushToTalkHotkey"]);
        Assert.Equal("", (string?)root["Recording"]!["ToggleHotkey"]);
    }

    [Fact]
    public void TryMigrate_MissingMode_DefaultsToPushToTalk()
    {
        var root = Parse("""{ "Recording": { "Hotkey": "Alt+D" } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        Assert.Equal("Alt+D", (string?)root["Recording"]!["PushToTalkHotkey"]);
        Assert.Equal("", (string?)root["Recording"]!["ToggleHotkey"]);
    }

    [Fact]
    public void TryMigrate_IsCaseInsensitive()
    {
        var root = Parse("""{ "recording": { "mode": "toggle", "hotkey": "Ctrl+Shift+D" } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        Assert.Equal("Ctrl+Shift+D", (string?)root["recording"]!["ToggleHotkey"]);
        Assert.Equal("", (string?)root["recording"]!["PushToTalkHotkey"]);
    }

    [Fact]
    public void TryMigrate_NewShape_IsLeftUntouched()
    {
        var json = """{ "Recording": { "PushToTalkHotkey": "F12", "ToggleHotkey": "Ctrl+Alt+D" } }""";
        var root = Parse(json);

        Assert.False(CreateMigration().TryMigrate(root));
        Assert.Equal(Parse(json).ToJsonString(), root.ToJsonString());
    }

    [Fact]
    public void TryMigrate_NoRecordingSection_IsLeftUntouched()
    {
        var root = Parse("""{ "ActiveProviders": { "Transcription": "Mock" } }""");

        Assert.False(CreateMigration().TryMigrate(root));
    }

    [Fact]
    public void TryMigrate_IsIdempotent()
    {
        var root = Parse("""{ "Recording": { "Mode": "Toggle", "Hotkey": "Ctrl+Shift+D" } }""");
        var migration = CreateMigration();

        Assert.True(migration.TryMigrate(root));
        var afterFirst = root.ToJsonString();

        Assert.False(migration.TryMigrate(root));
        Assert.Equal(afterFirst, root.ToJsonString());
    }

    [Fact]
    public async Task LoadAsync_LegacyRecording_MigratesWritesBackAndLoads()
    {
        using var paths = new TestAppPaths();
        await File.WriteAllTextAsync(
            paths.SettingsFilePath,
            """{ "Recording": { "Mode": "Toggle", "Hotkey": "Ctrl+Shift+D" } }""");
        var service = new SettingsService(paths, [CreateMigration()], NullLogger<SettingsService>.Instance);

        await service.LoadAsync();

        Assert.Equal("Ctrl+Shift+D", service.Current.Recording.ToggleHotkey);
        Assert.Equal("", service.Current.Recording.PushToTalkHotkey);

        // The file on disk was rewritten in the new shape, so the migration runs once.
        var written = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(paths.SettingsFilePath))!;
        Assert.Null(written["Recording"]!["Mode"]);
        Assert.Null(written["Recording"]!["Hotkey"]);
        Assert.Equal("Ctrl+Shift+D", (string?)written["Recording"]!["ToggleHotkey"]);
    }
}
