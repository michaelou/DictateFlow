using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Llm;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// The default <see cref="ILLMProvider"/>: delegates every call to the provider currently
/// active in settings, resolved through <see cref="IProviderRegistry"/> per call so a
/// settings change applies to the very next dictation. When the configured name is unknown
/// (a hand-edited settings file), the first registered provider is used instead with a
/// warning — a bad name must never break dictation.
/// </summary>
public sealed class ActiveLLMProvider : ILLMProvider
{
    private readonly IProviderRegistry _registry;
    private readonly ILogger<ActiveLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveLLMProvider"/> class.</summary>
    /// <param name="registry">Resolves the active provider, per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ActiveLLMProvider(IProviderRegistry registry, ILogger<ActiveLLMProvider> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
        => Resolve().ProcessAsync(context, cancellationToken);

    /// <summary>Resolves the active provider, falling back to the first registered one on an unknown name.</summary>
    private ILLMProvider Resolve()
    {
        try
        {
            return _registry.ResolveLLM();
        }
        catch (ProviderException ex)
        {
            var names = _registry.GetNames(ProviderKind.Llm);
            if (names.Count == 0)
            {
                throw;
            }

            _logger.LogWarning("{Message} Falling back to '{Fallback}'", ex.Message, names[0]);
            return _registry.Resolve<ILLMProvider>(ProviderKind.Llm, names[0]);
        }
    }
}
