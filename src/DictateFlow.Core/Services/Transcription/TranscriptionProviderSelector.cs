using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Transcription;

/// <summary>
/// Settings-based provider selection: delegates each transcription to the configured provider,
/// or to the mock provider when <see cref="SpeechSettings.Endpoint"/> is empty. Reads settings
/// on every call so a newly saved endpoint takes effect without a restart. A full provider
/// registry arrives in M7; until then this stays deliberately simple.
/// </summary>
public sealed class TranscriptionProviderSelector : ITranscriptionProvider
{
    private readonly ISettingsService _settingsService;
    private readonly Func<ITranscriptionProvider> _configuredFactory;
    private readonly Func<ITranscriptionProvider> _mockFactory;
    private readonly ILogger<TranscriptionProviderSelector> _logger;

    /// <summary>Initializes a new instance of the <see cref="TranscriptionProviderSelector"/> class.</summary>
    /// <param name="settingsService">Supplies the speech settings that drive the selection.</param>
    /// <param name="configuredFactory">Creates the real (configured) provider; invoked per call so typed <c>HttpClient</c> instances stay short-lived.</param>
    /// <param name="mockFactory">Creates the mock fallback provider.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TranscriptionProviderSelector(
        ISettingsService settingsService,
        Func<ITranscriptionProvider> configuredFactory,
        Func<ITranscriptionProvider> mockFactory,
        ILogger<TranscriptionProviderSelector> logger)
    {
        _settingsService = settingsService;
        _configuredFactory = configuredFactory;
        _mockFactory = mockFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var useMock = string.IsNullOrWhiteSpace(_settingsService.Current.Speech.Endpoint);
        _logger.LogDebug("Transcribing with the {Provider} provider", useMock ? "mock" : "configured");
        var provider = useMock ? _mockFactory() : _configuredFactory();
        return provider.TranscribeAsync(audio, cancellationToken);
    }
}
