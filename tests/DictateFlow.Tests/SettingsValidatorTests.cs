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
        catalog.Add(ProviderKind.Transcription, "Mock", typeof(object));
        catalog.Add(ProviderKind.Transcription, "AzureFoundry", typeof(object));
        catalog.Add(ProviderKind.Llm, "Mock", typeof(object));
        catalog.Add(ProviderKind.Llm, "AzureFoundry", typeof(object));
        catalog.Add(ProviderKind.Output, "ClipboardPaste", typeof(object));

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
    [InlineData("")]
    [InlineData("NotAKey+Chord")]
    [InlineData("Ctrl+")]
    public void InvalidHotkey_IsGeneralError(string hotkey)
    {
        var settings = new AppSettings();
        settings.Recording.Hotkey = hotkey;

        var finding = Single(CreateValidator("Raw").Validate(settings), "General");
        Assert.Equal(SettingsValidationSeverity.Error, finding.Severity);
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
    public void Findings_AreOrderedErrorsFirst()
    {
        var settings = new AppSettings { ActivePromptMode = "Vanished" }; // warning
        settings.Recording.Hotkey = "bogus"; // error

        var findings = CreateValidator("Raw").Validate(settings);

        Assert.Equal(2, findings.Count);
        Assert.Equal(SettingsValidationSeverity.Error, findings[0].Severity);
        Assert.Equal(SettingsValidationSeverity.Warning, findings[1].Severity);
    }
}
