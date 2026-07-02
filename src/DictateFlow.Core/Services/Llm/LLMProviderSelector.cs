using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Llm;

/// <summary>
/// Settings-based provider selection: delegates each enhancement to the configured provider,
/// or to the mock provider when <see cref="LlmSettings.Endpoint"/> is empty. Reads settings
/// on every call so a newly saved endpoint takes effect without a restart — the same pattern
/// as the M3 transcription selector.
/// </summary>
public sealed class LLMProviderSelector : ILLMProvider
{
    private readonly ISettingsService _settingsService;
    private readonly Func<ILLMProvider> _configuredFactory;
    private readonly Func<ILLMProvider> _mockFactory;
    private readonly ILogger<LLMProviderSelector> _logger;

    /// <summary>Initializes a new instance of the <see cref="LLMProviderSelector"/> class.</summary>
    /// <param name="settingsService">Supplies the LLM settings that drive the selection.</param>
    /// <param name="configuredFactory">Creates the real (configured) provider; invoked per call so typed <c>HttpClient</c> instances stay short-lived.</param>
    /// <param name="mockFactory">Creates the mock fallback provider.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public LLMProviderSelector(
        ISettingsService settingsService,
        Func<ILLMProvider> configuredFactory,
        Func<ILLMProvider> mockFactory,
        ILogger<LLMProviderSelector> logger)
    {
        _settingsService = settingsService;
        _configuredFactory = configuredFactory;
        _mockFactory = mockFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
    {
        var useMock = string.IsNullOrWhiteSpace(_settingsService.Current.Llm.Endpoint);
        _logger.LogDebug("Enhancing with the {Provider} LLM provider", useMock ? "mock" : "configured");
        var provider = useMock ? _mockFactory() : _configuredFactory();
        return provider.ProcessAsync(context, cancellationToken);
    }
}
