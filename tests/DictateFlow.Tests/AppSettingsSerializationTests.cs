using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;

namespace DictateFlow.Tests;

/// <summary>
/// Verifies that serialized <see cref="AppSettings"/> defaults match the schema defined
/// in the M1 issue, so the settings.json shape stays stable across milestones.
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
        Assert.Equal("PushToTalk", recording.GetProperty("Mode").GetString());
        Assert.Equal("Ctrl+Alt+D", recording.GetProperty("Hotkey").GetString());
        Assert.Equal(JsonValueKind.Null, recording.GetProperty("MicrophoneDeviceId").ValueKind);
        Assert.Equal(30, recording.GetProperty("SilenceTimeoutSeconds").GetInt32());

        var speech = root.GetProperty("Speech");
        Assert.Equal("", speech.GetProperty("Endpoint").GetString());
        Assert.Equal("", speech.GetProperty("ApiKey").GetString());
        Assert.Equal("", speech.GetProperty("DeploymentName").GetString());
        Assert.Equal("en-US", speech.GetProperty("Language").GetString());
        Assert.Equal(30, speech.GetProperty("TimeoutSeconds").GetInt32());

        var llm = root.GetProperty("Llm");
        Assert.Equal("", llm.GetProperty("Endpoint").GetString());
        Assert.Equal("", llm.GetProperty("ApiKey").GetString());
        Assert.Equal("", llm.GetProperty("DeploymentName").GetString());
        Assert.Equal(0.2, llm.GetProperty("Temperature").GetDouble());
        Assert.Equal(2000, llm.GetProperty("MaxTokens").GetInt32());
        Assert.Equal(60, llm.GetProperty("TimeoutSeconds").GetInt32());

        var output = root.GetProperty("Output");
        Assert.Equal("ClipboardPaste", output.GetProperty("Provider").GetString());
        Assert.Equal("Automatic", output.GetProperty("Mode").GetString());

        Assert.True(root.GetProperty("History").GetProperty("Enabled").GetBoolean());
        Assert.Equal("Raw", root.GetProperty("ActivePromptMode").GetString());
        Assert.Equal(0, root.GetProperty("TechnicalDictionary").GetArrayLength());
        Assert.Equal(0, root.GetProperty("ApplicationRules").GetArrayLength());
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
        const string json = """{ "LLM": { "temperature": 0.9 }, "OUTPUT": { "PROVIDER": "TypeText" } }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.SerializerOptions);

        Assert.NotNull(settings);
        Assert.Equal(0.9, settings.Llm.Temperature);
        Assert.Equal("TypeText", settings.Output.Provider);
    }
}
