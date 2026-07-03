using DictateFlow.Core.Services.Output;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// The default <see cref="IOutputProvider"/>: delegates every call to the provider currently
/// active in settings, resolved through <see cref="IProviderRegistry"/> per call so a
/// settings change applies to the very next dictation. When the configured name is unknown
/// (a hand-edited settings file), the first registered provider is used instead with a
/// warning — a bad name must never lose dictated text.
/// </summary>
public sealed class ActiveOutputProvider : IOutputProvider
{
    private readonly IProviderRegistry _registry;
    private readonly ILogger<ActiveOutputProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveOutputProvider"/> class.</summary>
    /// <param name="registry">Resolves the active provider, per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ActiveOutputProvider(IProviderRegistry registry, ILogger<ActiveOutputProvider> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Resolve().Name;

    /// <inheritdoc />
    public Task OutputAsync(string text) => Resolve().OutputAsync(text);

    /// <summary>Resolves the active provider, falling back to the first registered one on an unknown name.</summary>
    private IOutputProvider Resolve()
    {
        try
        {
            return _registry.ResolveOutput();
        }
        catch (ProviderException ex)
        {
            var names = _registry.GetNames(ProviderKind.Output);
            if (names.Count == 0)
            {
                throw;
            }

            _logger.LogWarning("{Message} Falling back to '{Fallback}'", ex.Message, names[0]);
            return _registry.Resolve<IOutputProvider>(ProviderKind.Output, names[0]);
        }
    }
}
