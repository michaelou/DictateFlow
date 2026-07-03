using System.Text.Json;
using DictateFlow.App.Services;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Transfer;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="SettingsTransfer"/>: export with/without secrets, round-trips and
/// pre-M7 schema import (reusing the M7 migration).
/// </summary>
public sealed class SettingsTransferTests
{
    private static SettingsTransfer CreateTransfer()
        => new(
            [
                new LegacySettingsMigration(NullLogger<LegacySettingsMigration>.Instance),
                new RecordingHotkeyMigration(NullLogger<RecordingHotkeyMigration>.Instance),
            ],
            NullLogger<SettingsTransfer>.Instance);

    /// <summary>Settings with configured Azure providers carrying API keys.</summary>
    private static AppSettings AzureSettings()
    {
        var settings = new AppSettings { ActivePromptMode = "Email" };
        settings.ActiveProviders.Transcription = "AzureFoundry";
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://speech.example.com",
            ApiKey = "speech-secret",
            DeploymentName = "transcribe",
        });
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://llm.example.com",
            ApiKey = "llm-secret",
            DeploymentName = "gpt",
        });
        settings.Recording.PushToTalkHotkey = "Ctrl+Shift+D";
        return settings;
    }

    [Fact]
    public void ExportJson_WithoutSecrets_BlanksEveryApiKey()
    {
        var json = CreateTransfer().ExportJson(AzureSettings(), includeSecrets: false);

        Assert.DoesNotContain("speech-secret", json);
        Assert.DoesNotContain("llm-secret", json);

        // The properties still exist — as empty strings — so a re-import keeps the shape.
        var imported = CreateTransfer().ParseImport(json);
        Assert.Equal("", imported.Providers.Transcription["AzureFoundry"].GetProperty("ApiKey").GetString());
        Assert.Equal("", imported.Providers.Llm["AzureFoundry"].GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void ExportJson_WithSecrets_KeepsApiKeys()
    {
        var json = CreateTransfer().ExportJson(AzureSettings(), includeSecrets: true);

        Assert.Contains("speech-secret", json);
        Assert.Contains("llm-secret", json);
    }

    [Fact]
    public void ExportWithoutSecrets_RoundTrip_KeepsAllNonSecretPreferences()
    {
        var transfer = CreateTransfer();

        var imported = transfer.ParseImport(transfer.ExportJson(AzureSettings(), includeSecrets: false));

        Assert.Equal("Ctrl+Shift+D", imported.Recording.PushToTalkHotkey);
        Assert.Equal("Email", imported.ActivePromptMode);
        Assert.Equal("AzureFoundry", imported.ActiveProviders.Transcription);
        Assert.Equal("https://speech.example.com",
            imported.Providers.Transcription["AzureFoundry"].GetProperty("Endpoint").GetString());
        Assert.Equal("transcribe",
            imported.Providers.Transcription["AzureFoundry"].GetProperty("DeploymentName").GetString());
    }

    [Fact]
    public void ExportJson_DoesNotMutateTheSourceSettings()
    {
        var settings = AzureSettings();

        CreateTransfer().ExportJson(settings, includeSecrets: false);

        Assert.Equal("speech-secret",
            settings.Providers.Transcription["AzureFoundry"].GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void ParseImport_PreM7Schema_MigratesToNamedProviders()
    {
        const string legacyJson = """
            {
              "Recording": { "Hotkey": "Ctrl+Shift+D" },
              "Speech": { "Endpoint": "https://speech.example.com", "ApiKey": "k1", "DeploymentName": "d1" },
              "Llm": { "Endpoint": "https://llm.example.com", "ApiKey": "k2", "DeploymentName": "d2" },
              "Output": { "Provider": "SimulatedKeyboard", "Mode": "Preview" }
            }
            """;

        var imported = CreateTransfer().ParseImport(legacyJson);

        Assert.Equal("AzureFoundry", imported.ActiveProviders.Transcription);
        Assert.Equal("AzureFoundry", imported.ActiveProviders.Llm);
        Assert.Equal("SimulatedKeyboard", imported.ActiveProviders.Output);
        Assert.Equal("https://speech.example.com",
            imported.Providers.Transcription["AzureFoundry"].GetProperty("Endpoint").GetString());
        Assert.Equal("Preview", imported.Output.Mode);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[ 1, 2, 3 ]")]
    public void ParseImport_InvalidContent_ThrowsJsonException(string content)
    {
        Assert.ThrowsAny<JsonException>(() => CreateTransfer().ParseImport(content));
    }

    [Fact]
    public void WindowPlacements_RoundTripThroughExport()
    {
        var transfer = CreateTransfer();
        var settings = new AppSettings();
        settings.WindowState["Settings"] = new WindowPlacement { Left = 100, Top = 60, Width = 800, Height = 500 };

        var imported = transfer.ParseImport(transfer.ExportJson(settings, includeSecrets: false));

        var placement = imported.WindowState["Settings"];
        Assert.Equal(100, placement.Left);
        Assert.Equal(60, placement.Top);
        Assert.Equal(800, placement.Width);
        Assert.Equal(500, placement.Height);
    }

    [Fact]
    public void GeneralSettings_RoundTripThroughExport()
    {
        var transfer = CreateTransfer();
        var settings = new AppSettings();
        settings.General.LaunchAtStartup = true;
        settings.General.FirstRunCompleted = true;

        var imported = transfer.ParseImport(transfer.ExportJson(settings, includeSecrets: false));

        Assert.True(imported.General.LaunchAtStartup);
        Assert.True(imported.General.FirstRunCompleted);
    }
}
