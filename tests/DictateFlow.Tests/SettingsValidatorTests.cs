using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Validation;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="SettingsValidator"/>: every rule of M8 §1 — valid settings pass and
/// each invalid case produces a finding with the right section and severity.
/// </summary>
public sealed class SettingsValidatorTests
{
    /// <summary>Builds a validator over the production provider set and the given known prompt modes.</summary>
    private static SettingsValidator CreateValidator(params string[] knownModes)
    {
        var catalog = new ProviderCatalog();
        catalog.Add(ProviderKind.Transcription, "Mock", typeof(object), requiresConnection: false);
        catalog.Add(ProviderKind.Transcription, "AzureFoundry", typeof(object));
        catalog.Add(ProviderKind.Transcription, "WhisperCpp", typeof(object), requiresConnection: false);
        catalog.Add(ProviderKind.Llm, "Mock", typeof(object), requiresConnection: false);
        catalog.Add(ProviderKind.Llm, "AzureFoundry", typeof(object));
        catalog.Add(ProviderKind.Output, "ClipboardPaste", typeof(object), requiresConnection: false);

        var store = new Mock<IPromptModeStore>();
        store.Setup(s => s.GetByName(It.IsAny<string>()))
            .Returns((string name) => knownModes.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? new PromptMode(name, "", "prompt", null)
                : null);
        return new SettingsValidator(catalog, store.Object);
    }

    private static AppSettings ValidAzureSettings()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "AzureFoundry";
        settings.ActiveProviders.Llm = "AzureFoundry";
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://speech.example.com",
            ApiKey = "key",
            DeploymentName = "transcribe",
            TimeoutSeconds = 30,
        });
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://llm.example.com",
            ApiKey = "key",
            DeploymentName = "gpt",
            Temperature = 0.2,
            MaxTokens = 2000,
            TimeoutSeconds = 60,
        });
        return settings;
    }

    private static SettingsValidationError Single(
        IReadOnlyList<SettingsValidationError> findings, string section)
        => Assert.Single(findings, f => f.Section == section);

    [Fact]
    public void DefaultSettings_HaveNoFindings()
    {
        var findings = CreateValidator("Raw").Validate(new AppSettings());

        Assert.Empty(findings);
    }

    [Fact]
    public void FullyConfiguredAzureSettings_HaveNoFindings()
    {
        var findings = CreateValidator("Raw").Validate(ValidAzureSettings());

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("NotAKey+Chord")]
    [InlineData("Ctrl+")]
    public void InvalidHotkey_IsGeneralError(string hotkey)
    {
        var settings = new AppSettings();
        settings.Recording.PushToTalkHotkey = hotkey;

        var finding = Single(CreateValidator("Raw").Validate(settings), "General");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void BothHotkeysEmpty_IsGeneralError()
    {
        var settings = new AppSettings();
        settings.Recording.PushToTalkHotkey = "";
        settings.Recording.ToggleHotkey = "";

        var finding = Single(CreateValidator("Raw").Validate(settings), "General");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
        Assert.Contains("at least one", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdenticalHotkeys_IsGeneralError()
    {
        var settings = new AppSettings();
        settings.Recording.PushToTalkHotkey = "Ctrl+Alt+D";
        settings.Recording.ToggleHotkey = "Ctrl+Alt+D";

        var finding = Single(CreateValidator("Raw").Validate(settings), "General");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
        Assert.Contains("different", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToggleOnly_WithPushToTalkEmpty_HasNoFindings()
    {
        var settings = new AppSettings();
        settings.Recording.PushToTalkHotkey = "";
        settings.Recording.ToggleHotkey = "Ctrl+Alt+D";

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Fact]
    public void TwoDistinctValidHotkeys_HaveNoFindings()
    {
        var settings = new AppSettings();
        settings.Recording.PushToTalkHotkey = "F12";
        settings.Recording.ToggleHotkey = "Ctrl+Alt+D";

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void SilenceTimeoutOutOfRange_IsGeneralError(int seconds)
    {
        var settings = new AppSettings();
        settings.Recording.SilenceTimeoutSeconds = seconds;

        var finding = Single(CreateValidator("Raw").Validate(settings), "General");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void UnknownTranscriptionProvider_IsSpeechError()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "DoesNotExist";

        var finding = Single(CreateValidator("Raw").Validate(settings), "Speech");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
        Assert.Contains("DoesNotExist", finding.Message);
    }

    [Fact]
    public void UnknownOutputProvider_IsOutputError()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Output = "Teleport";

        var finding = Single(CreateValidator("Raw").Validate(settings), "Output");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void ActiveLocalProvider_NeedsNoConnectionFields()
    {
        // Regression: with Local Whisper.cpp active, Save must not demand the cloud-only
        // Endpoint/ApiKey/DeploymentName fields.
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "WhisperCpp";
        settings.Providers.Transcription["WhisperCpp"] = JsonSerializer.SerializeToElement(new
        {
            Model = "ggml-small",
            Language = "",
            Threads = 0,
            TimeoutSeconds = 120,
        });

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Fact]
    public void ActiveLocalProviderWithoutConfigSection_HasNoFindings()
    {
        // A local provider works from defaults; a missing section is not an error.
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "WhisperCpp";

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void LocalProviderTimeoutOutOfRange_IsStillSpeechError(int timeoutSeconds)
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "WhisperCpp";
        settings.Providers.Transcription["WhisperCpp"] = JsonSerializer.SerializeToElement(new
        {
            Model = "ggml-small",
            TimeoutSeconds = timeoutSeconds,
        });

        var finding = Single(CreateValidator("Raw").Validate(settings), "Speech");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void LocalProviderMalformedLanguage_IsStillSpeechWarning()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Transcription = "WhisperCpp";
        settings.Providers.Transcription["WhisperCpp"] = JsonSerializer.SerializeToElement(new
        {
            Model = "ggml-small",
            Language = "greek",
        });

        var finding = Single(CreateValidator("Raw").Validate(settings), "Speech");
        Assert.Equal(SettingsValidationSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void ActiveNonMockProviderWithoutConfigSection_IsError()
    {
        var settings = new AppSettings();
        settings.ActiveProviders.Llm = "AzureFoundry"; // no Providers.Llm.AzureFoundry section

        var finding = Single(CreateValidator("Raw").Validate(settings), "LLM");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void ActiveNonMockProviderWithEmptyFields_ReportsEachMissingField()
    {
        var settings = ValidAzureSettings();
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "",
            ApiKey = "",
            DeploymentName = "",
        });

        var findings = CreateValidator("Raw").Validate(settings);

        Assert.Equal(3, findings.Count(f => f.Section == "Speech"));
        Assert.All(findings, f => Assert.Equal(SettingsValidationSeverity.Error, f.Severity));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-US, el-GR")]
    [InlineData("zh-Hans-CN")]
    [InlineData("")] // empty = auto-detect
    public void WellFormedLanguages_HaveNoFindings(string language)
    {
        var settings = ValidAzureSettings();
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://speech.example.com",
            ApiKey = "key",
            DeploymentName = "transcribe",
            Language = language,
        });

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Theory]
    [InlineData("english", "english")]
    [InlineData("en-US; el-GR", "en-US; el-GR")] // wrong separator
    [InlineData("en-US, greek", "greek")]
    public void MalformedLanguageEntry_IsSpeechWarning(string language, string badEntry)
    {
        var settings = ValidAzureSettings();
        settings.Providers.Transcription["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://speech.example.com",
            ApiKey = "key",
            DeploymentName = "transcribe",
            Language = language,
        });

        var finding = Single(CreateValidator("Raw").Validate(settings), "Speech");
        Assert.Equal(SettingsValidationSeverity.Warning, finding.Severity);
        Assert.Contains(badEntry, finding.Message);
    }

    [Theory]
    [InlineData("http://insecure.example.com")] // https required
    [InlineData("not-a-url")]
    public void NonHttpsEndpoint_IsError(string endpoint)
    {
        var settings = ValidAzureSettings();
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = endpoint,
            ApiKey = "key",
            DeploymentName = "gpt",
        });

        var finding = Single(CreateValidator("Raw").Validate(settings), "LLM");
        Assert.Contains(endpoint, finding.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void TemperatureOutOfRange_IsLlmError(double temperature)
    {
        var settings = ValidAzureSettings();
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://llm.example.com",
            ApiKey = "key",
            DeploymentName = "gpt",
            Temperature = temperature,
        });

        Single(CreateValidator("Raw").Validate(settings), "LLM");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128001)]
    public void MaxTokensOutOfRange_IsLlmError(int maxTokens)
    {
        var settings = ValidAzureSettings();
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://llm.example.com",
            ApiKey = "key",
            DeploymentName = "gpt",
            MaxTokens = maxTokens,
        });

        Single(CreateValidator("Raw").Validate(settings), "LLM");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void TimeoutOutOfRange_IsError_EvenOnTheMockSection(int timeoutSeconds)
    {
        // Numeric ranges apply to whatever the active section declares — including mocks.
        var settings = new AppSettings();
        settings.Providers.Transcription["Mock"] = JsonSerializer.SerializeToElement(new
        {
            DelayMs = 300,
            TimeoutSeconds = timeoutSeconds,
        });

        Single(CreateValidator("Raw").Validate(settings), "Speech");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void HistoryMaxEntriesBelowOne_IsHistoryError(int maxEntries)
    {
        var settings = new AppSettings();
        settings.History.MaxEntries = maxEntries;

        var finding = Single(CreateValidator("Raw").Validate(settings), "History");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void NegativePricingRate_IsPricingError()
    {
        var settings = new AppSettings();
        settings.Pricing.LlmPromptPer1M = -1;

        var finding = Single(CreateValidator("Raw").Validate(settings), "Pricing");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void UnknownActivePromptMode_IsPromptsWarning()
    {
        var settings = new AppSettings { ActivePromptMode = "Vanished" };

        var finding = Single(CreateValidator("Raw").Validate(settings), "Prompts");
        Assert.Equal(SettingsValidationSeverity.Warning, finding.Severity);
        Assert.Contains("Vanished", finding.Message);
    }

    [Fact]
    public void RuleWithUnknownPromptMode_IsRulesWarning()
    {
        var settings = new AppSettings
        {
            ApplicationRules =
            [
                new ApplicationRule { ProcessName = "OUTLOOK", PromptMode = "Email" },
                new ApplicationRule { ProcessName = "devenv", PromptMode = "Raw" },
            ],
        };

        var finding = Single(CreateValidator("Raw").Validate(settings), "Rules");
        Assert.Equal(SettingsValidationSeverity.Warning, finding.Severity);
        Assert.Contains("Email", finding.Message);
    }

    [Fact]
    public void VoiceCommandsEnabledWithBlankWakePhrase_IsVoiceCommandsError()
    {
        var settings = new AppSettings();
        settings.VoiceCommands.Enabled = true;
        settings.VoiceCommands.WakePhrase = "  ";

        var finding = Single(CreateValidator("Raw").Validate(settings), "Voice Commands");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
        Assert.Contains("wake phrase", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VoiceCommandsBlankWakePhraseWhileWakePhraseDisabled_HasNoFindings()
    {
        var settings = new AppSettings();
        settings.VoiceCommands.Enabled = true;
        settings.VoiceCommands.WakePhrase = "";
        settings.VoiceCommands.WakePhraseEnabled = false;

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void VoiceCommandTimeoutOutOfRange_IsVoiceCommandsError(int seconds)
    {
        var settings = new AppSettings();
        settings.VoiceCommands.Enabled = true;
        settings.VoiceCommands.CommandTimeoutSeconds = seconds;

        var finding = Single(CreateValidator("Raw").Validate(settings), "Voice Commands");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
        Assert.Contains("timeout", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VoiceCommandsDisabled_MisconfigurationStaysSilent()
    {
        // The feature is off: nothing can execute, so a broken section is not worth a finding.
        var settings = new AppSettings();
        settings.VoiceCommands.Enabled = false;
        settings.VoiceCommands.WakePhrase = "";
        settings.VoiceCommands.CommandTimeoutSeconds = 0;

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Fact]
    public void VoiceCommandsEnabledDefaults_HaveNoFindings()
    {
        var settings = new AppSettings();
        settings.VoiceCommands.Enabled = true;

        Assert.Empty(CreateValidator("Raw").Validate(settings));
    }

    [Fact]
    public void Findings_AreOrderedErrorsFirst()
    {
        var settings = new AppSettings { ActivePromptMode = "Vanished" }; // warning
        settings.Recording.PushToTalkHotkey = "bogus"; // error

        var findings = CreateValidator("Raw").Validate(settings);

        Assert.Equal(2, findings.Count);
        Assert.Equal(SettingsValidationSeverity.Error, findings[0].Severity);
        Assert.Equal(SettingsValidationSeverity.Warning, findings[1].Severity);
    }
}
