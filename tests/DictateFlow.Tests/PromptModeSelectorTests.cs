using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="PromptModeSelector"/>: rule matching and the active-mode fallback.</summary>
public sealed class PromptModeSelectorTests
{
    private readonly AppSettings _appSettings = new() { ActivePromptMode = "Raw" };
    private readonly PromptModeSelector _selector;

    public PromptModeSelectorTests()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        _selector = new PromptModeSelector(settings.Object, NullLogger<PromptModeSelector>.Instance);
    }

    private void AddRule(string processName, string promptMode)
        => _appSettings.ApplicationRules.Add(new ApplicationRule { ProcessName = processName, PromptMode = promptMode });

    [Fact]
    public void SelectMode_NoRules_ReturnsActiveMode()
    {
        Assert.Equal("Raw", _selector.SelectMode("OUTLOOK"));
    }

    [Fact]
    public void SelectMode_MatchingRule_ReturnsItsMode()
    {
        AddRule("OUTLOOK", "Email");

        Assert.Equal("Email", _selector.SelectMode("OUTLOOK"));
    }

    [Fact]
    public void SelectMode_MatchIsCaseInsensitive()
    {
        AddRule("outlook", "Email");

        Assert.Equal("Email", _selector.SelectMode("OUTLOOK"));
    }

    [Theory]
    [InlineData("OUTLOOK.exe", "OUTLOOK")] // .exe on the rule
    [InlineData("OUTLOOK", "OUTLOOK.EXE")] // .exe on the captured name
    [InlineData("outlook.EXE", "Outlook.exe")] // .exe on both, mixed case
    public void SelectMode_ExeSuffixIsIgnoredOnBothSides(string ruleName, string capturedName)
    {
        AddRule(ruleName, "Email");

        Assert.Equal("Email", _selector.SelectMode(capturedName));
    }

    [Fact]
    public void SelectMode_FirstMatchWins()
    {
        AddRule("OUTLOOK", "Email");
        AddRule("OUTLOOK", "ChatPrompt");

        Assert.Equal("Email", _selector.SelectMode("OUTLOOK"));
    }

    [Fact]
    public void SelectMode_NoMatch_FallsBackToActiveMode()
    {
        AddRule("devenv", "TechnicalSpec");
        _appSettings.ActivePromptMode = "ChatPrompt";

        Assert.Equal("ChatPrompt", _selector.SelectMode("OUTLOOK"));
    }

    [Fact]
    public void SelectMode_EmptyApplicationName_FallsBackToActiveMode()
    {
        AddRule("OUTLOOK", "Email");

        Assert.Equal("Raw", _selector.SelectMode(""));
    }

    [Fact]
    public void SelectMode_RuleWithEmptyMode_IsSkipped()
    {
        AddRule("OUTLOOK", "");
        AddRule("OUTLOOK", "Email");

        Assert.Equal("Email", _selector.SelectMode("OUTLOOK"));
    }

    [Fact]
    public void SelectMode_PartialProcessNameDoesNotMatch()
    {
        AddRule("OUTLOOK", "Email");

        Assert.Equal("Raw", _selector.SelectMode("OUT"));
    }
}
