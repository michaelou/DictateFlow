using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;

namespace DictateFlow.Tests;

/// <summary>
/// Verifies that serialized <see cref="AppSettings"/> defaults match the M7 named-provider
/// schema, so the settings.json shape stays stable across milestones.
/// </summary>
public sealed class AppSettingsSerializationTests
{
    [Fact]
    public void DefaultSettings_SerializeToExpectedSchema()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), SettingsService.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var recording = root.GetProperty("Recording");
        Assert.Equal("Ctrl+Alt+D", recording.GetProperty("PushToTalkHotkey").GetString());
        Assert.Equal("", recording.GetProperty("ToggleHotkey").GetString());
        Assert.Equal("", recording.GetProperty("DictatePadHotkey").GetString());
        Assert.Equal(JsonValueKind.Null, recording.GetProperty("MicrophoneDeviceId").ValueKind);
        Assert.Equal(30, recording.GetProperty("SilenceTimeoutSeconds").GetInt32());

        // Named providers replace the pre-M7 flat Speech/Llm sections.
        Assert.False(root.TryGetProperty("Speech", out _));
        Assert.False(root.TryGetProperty("Llm", out _));

        var activeProviders = root.GetProperty("ActiveProviders");
        Assert.Equal("Mock", activeProviders.GetProperty("Transcription").GetString());
        Assert.Equal("Mock", activeProviders.GetProperty("Llm").GetString());
        Assert.Equal("", activeProviders.GetProperty("Output").GetString()); // empty → first registered

        var providers = root.GetProperty("Providers");
        var mockTranscription = providers.GetProperty("Transcription").GetProperty("Mock");
        Assert.Equal(300, mockTranscription.GetProperty("DelayMs").GetInt32());
        Assert.StartsWith("This is mock transcript text", mockTranscription.GetProperty("Text").GetString());
        var mockLlm = providers.GetProperty("Llm").GetProperty("Mock");
        Assert.Equal(300, mockLlm.GetProperty("DelayMs").GetInt32());
        Assert.Equal(0, providers.GetProperty("Output").EnumerateObject().Count());

        var output = root.GetProperty("Output");
        Assert.Equal("Automatic", output.GetProperty("Mode").GetString());
        Assert.False(output.TryGetProperty("Provider", out _)); // moved to ActiveProviders.Output

        var history = root.GetProperty("History");
        Assert.True(history.GetProperty("Enabled").GetBoolean());
        Assert.Equal(1000, history.GetProperty("MaxEntries").GetInt32());

        var pricing = root.GetProperty("Pricing");
        Assert.Equal(0.006, pricing.GetProperty("SpeechPerMinute").GetDouble());
        Assert.Equal(2.50, pricing.GetProperty("LlmPromptPer1M").GetDouble());
        Assert.Equal(10.00, pricing.GetProperty("LlmCompletionPer1M").GetDouble());
        Assert.Equal("USD", pricing.GetProperty("Currency").GetString());

        Assert.Equal("Information", root.GetProperty("Logging").GetProperty("MinimumLevel").GetString());

        // Voice commands (issue #26) ship disabled with safe defaults.
        var voiceCommands = root.GetProperty("VoiceCommands");
        Assert.False(voiceCommands.GetProperty("Enabled").GetBoolean());
        Assert.Equal("Hey John", voiceCommands.GetProperty("WakePhrase").GetString());
        Assert.True(voiceCommands.GetProperty("WakePhraseEnabled").GetBoolean());
        Assert.Equal(30, voiceCommands.GetProperty("CommandTimeoutSeconds").GetInt32());
        Assert.False(voiceCommands.GetProperty("RequireConfirmation").GetBoolean());
        Assert.True(voiceCommands.GetProperty("EnableSounds").GetBoolean());

        Assert.Equal("Raw", root.GetProperty("ActivePromptMode").GetString());
        Assert.Equal(0, root.GetProperty("TechnicalDictionary").GetArrayLength());
        Assert.Equal(0, root.GetProperty("ApplicationRules").GetArrayLength());
    }

    [Fact]
    public void ProviderSections_RoundTripThroughJson()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "AzureFoundry";
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(
            new { Endpoint = "https://example.com", ApiKey = "key" });

        var json = JsonSerializer.Serialize(settings, SettingsService.SerializerOptions);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.SerializerOptions);

        Assert.NotNull(loaded);
        Assert.Equal("AzureFoundry", loaded.ActiveProviders.Transcription);
        var section = loaded.Providers.Transcription["AzureFoundry"];
        Assert.Equal("https://example.com", section.GetProperty("Endpoint").GetString());
        Assert.Equal("key", section.GetProperty("ApiKey").GetString());
        Assert.True(loaded.Providers.Transcription.ContainsKey("Mock")); // default section survives
    }

    [Fact]
    public void ApplicationRules_RoundTripThroughJson()
    {
        var settings = new AppSettings
        {
            ApplicationRules =
            [
                new ApplicationRule { ProcessName = "OUTLOOK", PromptMode = "Email" },
                new ApplicationRule { ProcessName = "devenv", PromptMode = "Raw" },
            ],
        };

        var json = JsonSerializer.Serialize(settings, SettingsService.SerializerOptions);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.SerializerOptions);

        Assert.NotNull(loaded);
        Assert.Collection(loaded.ApplicationRules,
            r => { Assert.Equal("OUTLOOK", r.ProcessName); Assert.Equal("Email", r.PromptMode); },
            r => { Assert.Equal("devenv", r.ProcessName); Assert.Equal("Raw", r.PromptMode); });
    }

    [Fact]
    public void DefaultSettings_SerializeIndented()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), SettingsService.SerializerOptions);

        Assert.Contains(Environment.NewLine, json);
    }

    [Fact]
    public void Deserialization_IsCaseInsensitive()
    {
        const string json = """{ "ACTIVEPROVIDERS": { "llm": "AzureFoundry" }, "OUTPUT": { "MODE": "Preview" } }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.SerializerOptions);

        Assert.NotNull(settings);
        Assert.Equal("AzureFoundry", settings.ActiveProviders.Llm);
        Assert.Equal("Preview", settings.Output.Mode);
    }
}
