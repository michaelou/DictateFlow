using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// The default <see cref="ITranscriptionProvider"/>: delegates every call to the provider
/// currently active in settings, resolved through <see cref="IProviderRegistry"/> per call so
/// a settings change applies to the very next dictation. When the configured name is unknown
/// (a hand-edited settings file), the first registered provider is used instead with a
/// warning — a bad name must never break dictation.
/// </summary>
public sealed class ActiveTranscriptionProvider : ITranscriptionProvider
{
    private readonly IProviderRegistry _registry;
    private readonly ILogger<ActiveTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveTranscriptionProvider"/> class.</summary>
    /// <param name="registry">Resolves the active provider, per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public ActiveTranscriptionProvider(IProviderRegistry registry, ILogger<ActiveTranscriptionProvider> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
        => Resolve().TranscribeAsync(audio, cancellationToken);

    /// <summary>Resolves the active provider, falling back to the first registered one on an unknown name.</summary>
    private ITranscriptionProvider Resolve()
    {
        try
        {
            return _registry.ResolveTranscription();
        }
        catch (ProviderException ex)
        {
            var names = _registry.GetNames(ProviderKind.Transcription);
            if (names.Count == 0)
            {
                throw;
            }

            _logger.LogWarning("{Message} Falling back to '{Fallback}'", ex.Message, names[0]);
            return _registry.Resolve<ITranscriptionProvider>(ProviderKind.Transcription, names[0]);
        }
    }
}
