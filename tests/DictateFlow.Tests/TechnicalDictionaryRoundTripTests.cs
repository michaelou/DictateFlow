using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// End-to-end check for the technical dictionary: terms saved through the settings service
/// survive a reload from disk and land in the resolved prompt via
/// <c>{{TechnicalDictionary}}</c>.
/// </summary>
public sealed class TechnicalDictionaryRoundTripTests : IDisposable
{
    private readonly TestAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task DictionaryTerms_SavedReloadedAndResolvedIntoThePrompt()
    {
        // Save terms through the real settings service…
        var writer = new SettingsService(_paths, [], NullLogger<SettingsService>.Instance);
        await writer.LoadAsync();
        writer.Current.TechnicalDictionary = ["Belugga", "DictateFlow", "xUnit"];
        await writer.SaveAsync();

        // …reload them with a fresh instance, as an app restart would…
        var reader = new SettingsService(_paths, [], NullLogger<SettingsService>.Instance);
        await reader.LoadAsync();
        Assert.Equal(["Belugga", "DictateFlow", "xUnit"], reader.Current.TechnicalDictionary);

        // …and resolve a prompt that uses the variable.
        var store = new Mock<IPromptModeStore>();
        store.Setup(s => s.GetByName("Email"))
            .Returns(new PromptMode("Email", "", "Never alter these terms: {{TechnicalDictionary}}.", null));
        var foregroundApp = new Mock<IForegroundAppService>();
        foregroundApp.SetupGet(f => f.LastCaptured).Returns("");
        var resolver = new PromptResolver(
            store.Object, reader, new TestProviderConfigReader(), foregroundApp.Object,
            TimeProvider.System, NullLogger<PromptResolver>.Instance);

        var context = resolver.Resolve("hello", "Email");

        Assert.Equal("Never alter these terms: Belugga, DictateFlow, xUnit.", context.SystemPrompt);
    }
}
