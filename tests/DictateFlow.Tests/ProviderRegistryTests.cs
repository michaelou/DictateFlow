using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for the keyed registration extensions, <see cref="ProviderCatalog"/>,
/// <see cref="ProviderRegistry"/> and the registry-backed default providers.
/// </summary>
public sealed class ProviderRegistryTests
{
    private sealed class TranscriptionA : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult("A", null, null));
    }

    private sealed class TranscriptionB : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult("B", null, null));
    }

    private sealed class LlmA : ILLMProvider
    {
        public Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
            => Task.FromResult("A");
    }

    private sealed class OutputA : IOutputProvider
    {
        public string? LastText { get; private set; }
        public string Name => "OutA";
        public Task OutputAsync(string text) { LastText = text; return Task.CompletedTask; }
    }

    private sealed class OutputB : IOutputProvider
    {
        public string Name => "OutB";
        public Task OutputAsync(string text) => Task.CompletedTask;
    }

    private readonly AppSettings _appSettings = new();

    private ServiceProvider BuildProvider(Action<IServiceCollection>? register = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings.Object);
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddTranscriptionProvider<TranscriptionA>("A");
        services.AddTranscriptionProvider<TranscriptionB>("B");
        services.AddLLMProvider<LlmA>("A");
        services.AddOutputProvider<OutputA>("OutA");
        services.AddOutputProvider<OutputB>("OutB");
        register?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void GetNames_EnumeratesPerKindInRegistrationOrder()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.Equal(["A", "B"], registry.GetNames(ProviderKind.Transcription));
        Assert.Equal(["A"], registry.GetNames(ProviderKind.Llm));
        Assert.Equal(["OutA", "OutB"], registry.GetNames(ProviderKind.Output));
    }

    [Fact]
    public void Resolve_ActiveNameFromSettings_ReturnsTheNamedProvider()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();
        _appSettings.ActiveProviders.Transcription = "B";

        Assert.IsType<TranscriptionB>(registry.ResolveTranscription());
    }

    [Fact]
    public void Resolve_NameMatchingIsCaseInsensitive()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();
        _appSettings.ActiveProviders.Transcription = "b";

        Assert.IsType<TranscriptionB>(registry.ResolveTranscription());
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsProviderExceptionListingValidNames()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();
        _appSettings.ActiveProviders.Transcription = "DoesNotExist";

        var ex = Assert.Throws<ProviderException>(() => registry.ResolveTranscription());

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("DoesNotExist", ex.Message);
        Assert.Contains("A", ex.Message);
        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void Resolve_SettingsChangeAtRuntime_NextResolveReturnsNewProvider()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        _appSettings.ActiveProviders.Transcription = "A";
        Assert.IsType<TranscriptionA>(registry.ResolveTranscription());

        _appSettings.ActiveProviders.Transcription = "B";
        Assert.IsType<TranscriptionB>(registry.ResolveTranscription());
    }

    [Fact]
    public void Resolve_EmptyName_ReturnsFirstRegisteredProvider()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();
        _appSettings.ActiveProviders.Output = "";

        Assert.IsType<OutputA>(registry.ResolveOutput());
    }

    [Fact]
    public void Resolve_KindWithoutRegistrations_ThrowsProviderException()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(_appSettings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings.Object);
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddTranscriptionProvider<TranscriptionA>("A"); // registers the catalog, but no output providers
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.Throws<ProviderException>(() => registry.ResolveOutput());
    }

    [Fact]
    public void Resolve_ByName_ReturnsTheNamedProviderRegardlessOfSettings()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();
        _appSettings.ActiveProviders.Transcription = "A";

        Assert.IsType<TranscriptionB>(registry.Resolve<ITranscriptionProvider>(ProviderKind.Transcription, "B"));
    }

    [Fact]
    public void AddProvider_DuplicateNamePerKind_Throws()
    {
        var services = new ServiceCollection();
        services.AddTranscriptionProvider<TranscriptionA>("A");

        Assert.Throws<InvalidOperationException>(() => services.AddTranscriptionProvider<TranscriptionB>("a"));
    }

    [Fact]
    public void AddProvider_SameNameForDifferentKinds_IsAllowed()
    {
        var services = new ServiceCollection();
        services.AddTranscriptionProvider<TranscriptionA>("A");
        services.AddLLMProvider<LlmA>("A"); // no throw

        var catalog = Assert.IsType<ProviderCatalog>(
            services.First(d => d.ServiceType == typeof(ProviderCatalog)).ImplementationInstance);
        Assert.Equal(2, catalog.Registrations.Count);
    }

    [Fact]
    public async Task ActiveProviders_DelegateToTheProviderActiveInSettings_PerCall()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<ITranscriptionProvider, ActiveTranscriptionProvider>());
        var active = provider.GetRequiredService<ITranscriptionProvider>();

        _appSettings.ActiveProviders.Transcription = "A";
        Assert.Equal("A", (await active.TranscribeAsync(new MemoryStream(), CancellationToken.None)).Text);

        // Runtime switch: the very next call uses the new provider, no restart.
        _appSettings.ActiveProviders.Transcription = "B";
        Assert.Equal("B", (await active.TranscribeAsync(new MemoryStream(), CancellationToken.None)).Text);
    }

    [Fact]
    public async Task ActiveTranscriptionProvider_UnknownName_FallsBackToFirstRegistered()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<ITranscriptionProvider, ActiveTranscriptionProvider>());
        var active = provider.GetRequiredService<ITranscriptionProvider>();
        _appSettings.ActiveProviders.Transcription = "DoesNotExist";

        // A bad name in a hand-edited settings file must never break dictation.
        var result = await active.TranscribeAsync(new MemoryStream(), CancellationToken.None);

        Assert.Equal("A", result.Text);
    }

    [Fact]
    public async Task ActiveOutputProvider_UnknownName_FallsBackToFirstRegistered()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IOutputProvider>(sp => new ActiveOutputProvider(
                sp.GetRequiredService<IProviderRegistry>(), NullLogger<ActiveOutputProvider>.Instance)));
        var active = provider.GetRequiredService<IOutputProvider>();
        _appSettings.ActiveProviders.Output = "DoesNotExist";

        await active.OutputAsync("text");

        Assert.Equal("text", provider.GetRequiredService<OutputA>().LastText);
    }
}
