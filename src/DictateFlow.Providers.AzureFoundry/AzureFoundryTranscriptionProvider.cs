using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.AzureFoundry;

/// <summary>
/// <see cref="ITranscriptionProvider"/> backed by the Azure AI Speech
/// <see href="https://learn.microsoft.com/azure/ai-services/speech-service/fast-transcription-create">Fast Transcription</see>
/// API (<c>/speechtotext/transcriptions:transcribe</c>). Reads its
/// <see cref="AzureFoundryTranscriptionConfig"/> section on every call, maps all transport
/// failures to <see cref="ProviderException"/>, and never logs the API key or the transcript
/// above Debug.
/// </summary>
public sealed class AzureFoundryTranscriptionProvider : ITranscriptionProvider
{
    /// <summary>API version sent when the endpoint does not already specify one. A settings override can be added later if needed.</summary>
    public const string DefaultApiVersion = "2025-10-15";

    /// <summary>Relative route for the Fast Transcription operation.</summary>
    private const string TranscribeRoute = "speechtotext/transcriptions:transcribe";

    /// <summary>Header carrying the Speech resource key.</summary>
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

    private const string ProviderName = AzureFoundryProviders.RegistrationName;

    /// <summary>Size of the WAV header; subtracted when computing duration from byte length.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly JsonSerializerOptions DefinitionJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureFoundryTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureFoundryTranscriptionProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the endpoint, key, deployment, language and timeout, read per call.</param>
    /// <param name="usageSink">Receives the audio duration after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureFoundryTranscriptionProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AzureFoundryTranscriptionProvider> logger)
    {
        _httpClient = httpClient;
        _configReader = configReader;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var speech = _configReader.GetConfig<AzureFoundryTranscriptionConfig>(ProviderKind.Transcription, ProviderName);
        var requestUri = BuildRequestUri(speech);

        if (string.IsNullOrWhiteSpace(speech.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your Azure AI Foundry API key in Settings → Speech.",
                isConfigurationError: true);
        }

        // Buffer the audio so the resilience pipeline can resend the body on retries.
        var audioBytes = await ReadAllBytesAsync(audio, cancellationToken).ConfigureAwait(false);

        // The user-facing timeout: enforced here (instead of at pipeline-build time) so a
        // changed Speech.TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (speech.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(speech.TimeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = BuildContent(audioBytes, speech.Language, speech.DeploymentName),
            };
            request.Headers.TryAddWithoutValidation(SubscriptionKeyHeader, speech.ApiKey);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug(
                "Transcription request finished: HTTP {StatusCode} in {ElapsedMs} ms for {AudioBytes} audio bytes",
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds, audioBytes.Length);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    ProviderName,
                    $"The speech service rejected the request ({(int)response.StatusCode}). Check your API key in Settings → Speech.",
                    isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(response, timeoutCts.Token).ConfigureAwait(false);
                throw new ProviderException(
                    ProviderName,
                    $"The speech service returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}){detail}. Try again; if it persists, check the endpoint and deployment name in Settings → Speech.");
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseResponse(json, audioBytes.Length, speech.Language);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller-initiated cancellation is not a provider failure.
        }
        catch (OperationCanceledException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"The transcription request timed out after {speech.TimeoutSeconds} s. Try again, or raise the timeout in Settings → Speech.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Could not reach the speech endpoint '{speech.Endpoint}' ({ex.Message}). Check the endpoint URL in Settings → Speech.",
                ex,
                isConfigurationError: true);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Transcription failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds the Fast Transcription request URI. A complete <c>…:transcribe</c> URL is used
    /// as-is (api-version added if missing); otherwise the endpoint is treated as the resource
    /// base and the <c>speechtotext/transcriptions:transcribe</c> route is appended.
    /// </summary>
    private Uri BuildRequestUri(AzureFoundryTranscriptionConfig speech)
    {
        var endpointText = speech.Endpoint.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderException(
                ProviderName,
                $"'{speech.Endpoint}' is not a valid http(s) URL. Check the endpoint in Settings → Speech.",
                isConfigurationError: true);
        }

        // A complete transcribe URL was pasted: use it as-is, adding api-version if missing.
        if (endpoint.AbsolutePath.EndsWith("transcriptions:transcribe", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Speech endpoint is a complete transcribe URL; using it as the full target URI");
            if (endpoint.Query.Contains("api-version", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            return new Uri($"{endpoint.AbsoluteUri}{separator}api-version={DefaultApiVersion}");
        }

        _logger.LogDebug("Speech endpoint is a resource base; appending the Fast Transcription route");
        var baseUrl = endpointText.TrimEnd('/');
        return new Uri($"{baseUrl}/{TranscribeRoute}?api-version={DefaultApiVersion}");
    }

    /// <summary>
    /// Builds the multipart/form-data body expected by the Fast Transcription endpoint: an
    /// <c>audio</c> file part and a <c>definition</c> JSON part. Field names are explicitly
    /// quoted — <see cref="MultipartFormDataContent"/> leaves them bare, which some multipart
    /// parsers reject. When a model (deployment) name is set it selects the enhanced model.
    /// </summary>
    private MultipartFormDataContent BuildContent(byte[] audioBytes, string language, string model)
    {
        var file = new ByteArrayContent(audioBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"audio\"",
            FileName = "\"audio.wav\"",
        };

        var enhanced = !string.IsNullOrWhiteSpace(model);
        var locales = ParseLocales(language);

        // The enhanced (LLM Speech) model rejects several candidate locales with HTTP 400; it
        // is multilingual by default, so omitting locales lets it detect the language itself.
        if (enhanced && locales is { Count: > 1 })
        {
            _logger.LogDebug(
                "Enhanced model with {LocaleCount} configured languages: omitting locales, the model auto-detects",
                locales.Count);
            locales = null;
        }

        var definitionJson = JsonSerializer.Serialize(
            new TranscribeDefinition(
                locales,
                enhanced ? new EnhancedModeDefinition(true, model.Trim(), "verbatim") : null),
            DefinitionJsonOptions);

        var definition = new StringContent(definitionJson);
        definition.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"definition\"" };

        return new MultipartFormDataContent { file, definition };
    }

    /// <summary>Parses the Fast Transcription JSON response, tolerating extra fields, with a WAV-length duration fallback.</summary>
    private TranscriptionResult ParseResponse(string json, int audioByteCount, string configuredLanguage)
    {
        FastTranscriptionResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<FastTranscriptionResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "The speech service returned an unreadable response.", ex);
        }

        // A valid Fast Transcription response always carries at least one of these fields; a
        // reply with neither is unrecognized. An empty transcript (e.g. silence) is valid and
        // must not fail — the "Test connection" check relies on that.
        if (response is null || (response.CombinedPhrases is null && response.DurationMilliseconds is null))
        {
            throw new ProviderException(ProviderName, "The speech service response did not contain a transcript.");
        }

        var text = response.CombinedPhrases is null
            ? ""
            : string.Join(" ", response.CombinedPhrases.Select(p => p.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)));

        var duration = response!.DurationMilliseconds is { } ms
            ? ms / 1000.0
            : Math.Max(0, audioByteCount - WavHeaderBytes) / (double)BytesPerSecond;

        // With several candidate locales (or auto-detect) the configured value is not the
        // spoken language, so only the service-reported locale is trustworthy then.
        var language = response.Phrases?.FirstOrDefault()?.Locale
            ?? (ParseLocales(configuredLanguage) is [var single] ? single : null);

        _usageSink.Record(new UsageRecord(
            _timeProvider.GetUtcNow().UtcDateTime,
            UsageCategories.Speech,
            duration,
            PromptTokens: null,
            CompletionTokens: null));

        _logger.LogDebug("Transcript received: {CharCount} characters, {DurationSeconds:F1} s of audio", text.Length, duration);
        return new TranscriptionResult(text, duration, language);
    }

    /// <summary>
    /// Best-effort extraction of a short, human-readable detail from an error response body,
    /// formatted for inline use in the exception message; empty when nothing usable is found.
    /// </summary>
    private static async Task<string> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return "";
        }

        // Azure errors are JSON like {"error":{"code":…,"message":…}}; fall back to the raw body.
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                body = message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
        }

        body = body.Trim();
        return body.Length switch
        {
            0 => "",
            <= 300 => $" — {body}",
            _ => $" — {body[..300]}…",
        };
    }

    /// <summary>
    /// Splits the configured language setting (comma-separated BCP-47 tags) into the candidate
    /// locale list; <see langword="null"/> when empty, which lets the service auto-detect.
    /// </summary>
    private static IReadOnlyList<string>? ParseLocales(string language)
    {
        var locales = language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return locales.Length == 0 ? null : locales;
    }

    /// <summary>Copies the audio stream (from its current position) into a byte array.</summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream audio, CancellationToken cancellationToken)
    {
        if (audio is MemoryStream memory && memory.Position == 0 && memory.TryGetBuffer(out var buffer))
        {
            return buffer.Array is not null && buffer.Offset == 0 && buffer.Count == buffer.Array.Length
                ? buffer.Array
                : buffer.AsSpan().ToArray();
        }

        using var copy = new MemoryStream();
        await audio.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }

    /// <summary>Wire shape of the <c>definition</c> form field sent with the request.</summary>
    private sealed record TranscribeDefinition(
        [property: JsonPropertyName("locales")] IReadOnlyList<string>? Locales,
        [property: JsonPropertyName("enhancedMode")] EnhancedModeDefinition? EnhancedMode);

    /// <summary>Selects an enhanced transcription model (e.g. MAI-Transcribe).</summary>
    private sealed record EnhancedModeDefinition(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("transcribeStyle")] string TranscribeStyle);

    /// <summary>Wire shape of the Fast Transcription response; unknown fields are ignored.</summary>
    private sealed record FastTranscriptionResponse(
        [property: JsonPropertyName("durationMilliseconds")] long? DurationMilliseconds,
        [property: JsonPropertyName("combinedPhrases")] IReadOnlyList<CombinedPhrase>? CombinedPhrases,
        [property: JsonPropertyName("phrases")] IReadOnlyList<TranscribedPhrase>? Phrases);

    /// <summary>The channel-merged transcript text.</summary>
    private sealed record CombinedPhrase(
        [property: JsonPropertyName("text")] string? Text);

    /// <summary>A recognized phrase; only its locale is consumed.</summary>
    private sealed record TranscribedPhrase(
        [property: JsonPropertyName("locale")] string? Locale);
}
