using System.Text.Json.Nodes;
using DictateFlow.App.Services;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="LegacySettingsMigration"/> (pre-M7 flat schema → named providers)
/// and its integration into <see cref="SettingsService.LoadAsync"/>.
/// </summary>
public sealed class SettingsMigrationTests
{
    /// <summary>A complete settings.json as written by the M6 app, with Azure configured.</summary>
    private const string LegacyM6Json = """
        {
          "Recording": { "Mode": "Toggle", "Hotkey": "Ctrl+Shift+D", "MicrophoneDeviceId": "mic-1", "SilenceTimeoutSeconds": 45 },
          "Speech": { "Endpoint": "https://speech.example.com", "ApiKey": "speech-key", "DeploymentName": "mai-transcribe", "Language": "de-DE", "TimeoutSeconds": 25 },
          "Llm": { "Endpoint": "https://llm.example.com", "ApiKey": "llm-key", "DeploymentName": "gpt-4o", "Temperature": 0.7, "MaxTokens": 1500, "TimeoutSeconds": 90 },
          "Output": { "Provider": "SimulatedKeyboard", "Mode": "Preview" },
          "History": { "Enabled": false, "MaxEntries": 42 },
          "Pricing": { "SpeechPerMinute": 0.01, "LlmPromptPer1M": 3.0, "LlmCompletionPer1M": 12.0, "Currency": "EUR" },
          "Logging": { "MinimumLevel": "Debug" },
          "ActivePromptMode": "Email",
          "TechnicalDictionary": [ "DictateFlow", "xUnit" ],
          "ApplicationRules": [ { "ProcessName": "OUTLOOK", "PromptMode": "Email" } ]
        }
        """;

    private static LegacySettingsMigration CreateMigration()
        => new(NullLogger<LegacySettingsMigration>.Instance);

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void TryMigrate_LegacyShape_MovesEveryValueIntoTheNewSchema()
    {
        var root = Parse(LegacyM6Json);

        Assert.True(CreateMigration().TryMigrate(root));

        // Flat sections are gone; ActiveProviders reflects the configured endpoints.
        Assert.False(root.ContainsKey("Speech"));
        Assert.False(root.ContainsKey("Llm"));
        Assert.Equal("AzureFoundry", (string?)root["ActiveProviders"]!["Transcription"]);
        Assert.Equal("AzureFoundry", (string?)root["ActiveProviders"]!["Llm"]);
        Assert.Equal("SimulatedKeyboard", (string?)root["ActiveProviders"]!["Output"]);
        Assert.Null(root["Output"]!["Provider"]);
        Assert.Equal("Preview", (string?)root["Output"]!["Mode"]);

        // The old sections became the AzureFoundry subsections, values intact.
        var speech = root["Providers"]!["Transcription"]!["AzureFoundry"]!;
        Assert.Equal("https://speech.example.com", (string?)speech["Endpoint"]);
        Assert.Equal("speech-key", (string?)speech["ApiKey"]);
        Assert.Equal("mai-transcribe", (string?)speech["DeploymentName"]);
        Assert.Equal("de-DE", (string?)speech["Language"]);
        Assert.Equal(25, (int?)speech["TimeoutSeconds"]);

        var llm = root["Providers"]!["Llm"]!["AzureFoundry"]!;
        Assert.Equal("https://llm.example.com", (string?)llm["Endpoint"]);
        Assert.Equal("llm-key", (string?)llm["ApiKey"]);
        Assert.Equal("gpt-4o", (string?)llm["DeploymentName"]);
        Assert.Equal(0.7, (double?)llm["Temperature"]);
        Assert.Equal(1500, (int?)llm["MaxTokens"]);
        Assert.Equal(90, (int?)llm["TimeoutSeconds"]);

        // Mock sections are seeded so the file documents the full shape.
        Assert.NotNull(root["Providers"]!["Transcription"]!["Mock"]);
        Assert.NotNull(root["Providers"]!["Llm"]!["Mock"]);
    }

    [Fact]
    public void TryMigrate_LegacyShape_LeavesTheM6SectionsUntouched()
    {
        var root = Parse(LegacyM6Json);

        CreateMigration().TryMigrate(root);

        Assert.Equal("Toggle", (string?)root["Recording"]!["Mode"]);
        Assert.Equal("mic-1", (string?)root["Recording"]!["MicrophoneDeviceId"]);
        Assert.False((bool)root["History"]!["Enabled"]!);
        Assert.Equal(42, (int?)root["History"]!["MaxEntries"]);
        Assert.Equal(0.01, (double?)root["Pricing"]!["SpeechPerMinute"]);
        Assert.Equal("EUR", (string?)root["Pricing"]!["Currency"]);
        Assert.Equal("Debug", (string?)root["Logging"]!["MinimumLevel"]);
        Assert.Equal("Email", (string?)root["ActivePromptMode"]);
        Assert.Equal(2, root["TechnicalDictionary"]!.AsArray().Count);
        Assert.Equal("OUTLOOK", (string?)root["ApplicationRules"]![0]!["ProcessName"]);
    }

    [Fact]
    public void TryMigrate_EmptyEndpoints_ActivateTheMockProviders()
    {
        // The pre-M7 behavior was "endpoint empty → mock"; migration preserves it.
        var root = Parse("""{ "Speech": { "Endpoint": "" }, "Llm": { "Endpoint": " " } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        Assert.Equal("Mock", (string?)root["ActiveProviders"]!["Transcription"]);
        Assert.Equal("Mock", (string?)root["ActiveProviders"]!["Llm"]);
        Assert.Equal("", (string?)root["ActiveProviders"]!["Output"]); // no Output section → first registered
    }

    [Fact]
    public void TryMigrate_NewShape_IsLeftUntouched()
    {
        var json = """{ "ActiveProviders": { "Transcription": "Mock" }, "Providers": {} }""";
        var root = Parse(json);

        Assert.False(CreateMigration().TryMigrate(root));
        Assert.Equal(Parse(json).ToJsonString(), root.ToJsonString());
    }

    [Fact]
    public void TryMigrate_FileWithNeitherShapeMarker_IsLeftUntouched()
    {
        var root = Parse("""{ "Recording": { "Hotkey": "Alt+D" } }""");

        Assert.False(CreateMigration().TryMigrate(root));
    }

    [Fact]
    public void TryMigrate_IsIdempotent()
    {
        var root = Parse(LegacyM6Json);
        var migration = CreateMigration();

        Assert.True(migration.TryMigrate(root));
        var afterFirst = root.ToJsonString();

        Assert.False(migration.TryMigrate(root));
        Assert.Equal(afterFirst, root.ToJsonString());
    }

    [Fact]
    public void TryMigrate_PropertyLookupIsCaseInsensitive()
    {
        var root = Parse("""{ "speech": { "endpoint": "https://x.example.com" }, "output": { "provider": "ClipboardPaste" } }""");

        Assert.True(CreateMigration().TryMigrate(root));

        Assert.Equal("AzureFoundry", (string?)root["ActiveProviders"]!["Transcription"]);
        Assert.Equal("ClipboardPaste", (string?)root["ActiveProviders"]!["Output"]);
        Assert.Equal(
            "https://x.example.com",
            (string?)root["Providers"]!["Transcription"]!["AzureFoundry"]!["endpoint"]);
    }

    [Fact]
    public async Task LoadAsync_LegacyFile_MigratesWritesBackAndLoads()
    {
        using var paths = new TestAppPaths();
        await File.WriteAllTextAsync(paths.SettingsFilePath, LegacyM6Json);
        var service = new SettingsService(
            paths, [CreateMigration()], NullLogger<SettingsService>.Instance);

        await service.LoadAsync();

        // The loaded settings carry the migrated values...
        Assert.Equal("AzureFoundry", service.Current.ActiveProviders.Transcription);
        Assert.Equal("SimulatedKeyboard", service.Current.ActiveProviders.Output);
        Assert.Equal("Preview", service.Current.Output.Mode);
        Assert.Equal("Email", service.Current.ActivePromptMode);
        var reader = new ProviderConfigReader(service, NullLogger<ProviderConfigReader>.Instance);
        Assert.Equal(
            "https://speech.example.com",
            reader.GetConfig<AzureFoundryTranscriptionConfig>(
                ProviderKind.Transcription, AzureFoundryProviders.RegistrationName).Endpoint);
        Assert.Equal(
            0.7,
            reader.GetConfig<AzureFoundryLlmConfig>(
                ProviderKind.Llm, AzureFoundryProviders.RegistrationName).Temperature);

        // ...and the file on disk was rewritten in the new shape, so the migration runs once.
        var written = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(paths.SettingsFilePath))!;
        Assert.False(written.ContainsKey("Speech"));
        Assert.NotNull(written["ActiveProviders"]);

        var reloading = new SettingsService(paths, [CreateMigration()], NullLogger<SettingsService>.Instance);
        await reloading.LoadAsync();
        Assert.Equal("AzureFoundry", reloading.Current.ActiveProviders.Transcription);
    }

    [Fact]
    public async Task LoadAsync_NewShapeFile_IsNotRewritten()
    {
        using var paths = new TestAppPaths();
        var writer = new SettingsService(paths, [], NullLogger<SettingsService>.Instance);
        await writer.SaveAsync();
        var originalWriteTime = File.GetLastWriteTimeUtc(paths.SettingsFilePath);

        var service = new SettingsService(paths, [CreateMigration()], NullLogger<SettingsService>.Instance);
        await service.LoadAsync();

        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(paths.SettingsFilePath));
    }
}
