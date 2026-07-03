using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// Default <see cref="IProviderRegistry"/> implementation over the
/// <see cref="ProviderCatalog"/> and keyed DI. An empty active name selects the first
/// registered provider of the kind (the built-in default); an unknown name is a
/// configuration error and throws a <see cref="ProviderException"/> naming the valid options.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ProviderCatalog _catalog;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ProviderRegistry> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProviderRegistry"/> class.</summary>
    /// <param name="serviceProvider">Resolves the keyed provider registrations.</param>
    /// <param name="catalog">Enumerates what was registered.</param>
    /// <param name="settingsService">Supplies the active provider names, read per resolve.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ProviderRegistry(
        IServiceProvider serviceProvider,
        ProviderCatalog catalog,
        ISettingsService settingsService,
        ILogger<ProviderRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetNames(ProviderKind kind) => _catalog.GetNames(kind);

    /// <inheritdoc />
    public T Resolve<T>(ProviderKind kind, string name) where T : class
    {
        var names = _catalog.GetNames(kind);
        string key;
        if (string.IsNullOrWhiteSpace(name))
        {
            key = names.Count > 0
                ? names[0]
                : throw new ProviderException(
                    kind.ToString(), $"No {kind} providers are registered.", isConfigurationError: true);
            _logger.LogDebug("No active {Kind} provider configured; using the default '{Name}'", kind, key);
        }
        else if (_catalog.TryGetRegisteredName(kind, name, out var registered))
        {
            key = registered;
        }
        else
        {
            throw new ProviderException(
                name,
                $"Unknown {kind} provider '{name}'. Valid providers: {string.Join(", ", names)}.",
                isConfigurationError: true);
        }

        return _serviceProvider.GetRequiredKeyedService<T>(key);
    }

    /// <inheritdoc />
    public ITranscriptionProvider ResolveTranscription()
        => Resolve<ITranscriptionProvider>(ProviderKind.Transcription, _settingsService.Current.ActiveProviders.Transcription);

    /// <inheritdoc />
    public ILLMProvider ResolveLLM()
        => Resolve<ILLMProvider>(ProviderKind.Llm, _settingsService.Current.ActiveProviders.Llm);

    /// <inheritdoc />
    public IOutputProvider ResolveOutput()
        => Resolve<IOutputProvider>(ProviderKind.Output, _settingsService.Current.ActiveProviders.Output);
}
