using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="SettingsService"/> (migration behavior is covered in <see cref="SettingsMigrationTests"/>).</summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly Mock<ILogger<SettingsService>> _logger = new();

    private SettingsService CreateService() => new(_paths, [], _logger.Object);

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task LoadAsync_WhenFileMissing_UsesDefaults()
    {
        var service = CreateService();

        await service.LoadAsync();

        Assert.Equal("Ctrl+Alt+D", service.Current.Recording.PushToTalkHotkey);
        Assert.Equal("", service.Current.Recording.ToggleHotkey);
        Assert.True(service.Current.History.Enabled);
        Assert.False(File.Exists(_paths.SettingsFilePath));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsModifiedValues()
    {
        var writer = CreateService();
        writer.Current.Recording.PushToTalkHotkey = "Ctrl+Shift+Space";
        writer.Current.ActiveProviders.Llm = "AzureFoundry";
        writer.Current.TechnicalDictionary.Add("Kubernetes");
        writer.Current.ActivePromptMode = "Professional";
        await writer.SaveAsync();

        var reader = CreateService();
        await reader.LoadAsync();

        Assert.Equal("Ctrl+Shift+Space", reader.Current.Recording.PushToTalkHotkey);
        Assert.Equal("AzureFoundry", reader.Current.ActiveProviders.Llm);
        Assert.Equal(["Kubernetes"], reader.Current.TechnicalDictionary);
        Assert.Equal("Professional", reader.Current.ActivePromptMode);
    }

    [Fact]
    public async Task LoadAsync_WhenFileCorrupt_FallsBackToDefaults()
    {
        await File.WriteAllTextAsync(_paths.SettingsFilePath, "{ this is not valid json !!!");
        var service = CreateService();

        await service.LoadAsync();

        Assert.Equal("Ctrl+Alt+D", service.Current.Recording.PushToTalkHotkey);
        Assert.Equal("Mock", service.Current.ActiveProviders.Transcription);
    }

    [Fact]
    public async Task LoadAsync_ReadsCamelCasePropertyNames()
    {
        await File.WriteAllTextAsync(
            _paths.SettingsFilePath,
            """{ "recording": { "pushToTalkHotkey": "Alt+Space" }, "activePromptMode": "Casual", "activeProviders": { "output": "Null" } }""");
        var service = CreateService();

        await service.LoadAsync();

        Assert.Equal("Alt+Space", service.Current.Recording.PushToTalkHotkey);
        Assert.Equal("Casual", service.Current.ActivePromptMode);
        Assert.Equal("Null", service.Current.ActiveProviders.Output);
    }

    [Fact]
    public async Task SaveAsync_RaisesSettingsChanged()
    {
        var service = CreateService();
        AppSettings? observed = null;
        service.SettingsChanged += (_, settings) => observed = settings;

        await service.SaveAsync();

        Assert.Same(service.Current, observed);
        Assert.True(File.Exists(_paths.SettingsFilePath));
    }

    [Fact]
    public async Task LoadAsync_RaisesSettingsChanged()
    {
        var service = CreateService();
        var raised = false;
        service.SettingsChanged += (_, _) => raised = true;

        await service.LoadAsync();

        Assert.True(raised);
    }
}
