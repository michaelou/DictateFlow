using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.AzureFoundry;

/// <summary>
/// <see cref="ITranscriptionProvider"/> backed by an Azure AI Foundry deployment
/// (e.g. MAI-Transcribe) through the OpenAI-compatible audio transcription surface.
/// Reads <see cref="SpeechSettings"/> on every call, maps all transport failures to
/// <see cref="ProviderException"/>, and never logs the API key or the transcript above Debug.
/// </summary>
public sealed class AzureFoundryTranscriptionProvider : ITranscriptionProvider
{
    /// <summary>API version sent when the endpoint does not already specify one. A settings override can be added later if needed.</summary>
    public const string DefaultApiVersion = "2024-06-01";

    private const string ProviderName = "AzureFoundry";

    /// <summary>Size of the WAV header; subtracted when computing duration from byte length.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureFoundryTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureFoundryTranscriptionProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="settingsService">Supplies the speech endpoint, key, deployment, language and timeout.</param>
    /// <param name="usageSink">Receives the audio duration after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureFoundryTranscriptionProvider(
        HttpClient httpClient,
        ISettingsService settingsService,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AzureFoundryTranscriptionProvider> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var speech = _settingsService.Current.Speech;
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
                Content = BuildContent(audioBytes, speech.Language),
            };
            request.Headers.TryAddWithoutValidation("api-key", speech.ApiKey);

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
                throw new ProviderException(
                    ProviderName,
                    $"The speech service returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Try again; if it persists, check the endpoint and deployment name in Settings → Speech.");
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
    /// Builds the request URI. When the configured endpoint is just a host, the standard
    /// deployments route is appended; when the user pasted a complete target URI (a path is
    /// already present), it is used as-is and only the api-version query is added if missing.
    /// </summary>
    private Uri BuildRequestUri(SpeechSettings speech)
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

        if (endpoint.AbsolutePath.Length > 1)
        {
            _logger.LogDebug("Speech endpoint contains a path; using it as the full target URI");
            if (endpoint.Query.Contains("api-version", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            return new Uri($"{endpoint.AbsoluteUri}{separator}api-version={DefaultApiVersion}");
        }

        if (string.IsNullOrWhiteSpace(speech.DeploymentName))
        {
            throw new ProviderException(
                ProviderName,
                "No deployment name is configured. Enter the model deployment name in Settings → Speech.",
                isConfigurationError: true);
        }

        _logger.LogDebug("Speech endpoint is a bare host; building the deployments route");
        var baseUrl = endpointText.TrimEnd('/');
        return new Uri(
            $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(speech.DeploymentName.Trim())}/audio/transcriptions?api-version={DefaultApiVersion}");
    }

    /// <summary>
    /// Builds the multipart/form-data body expected by the transcription endpoint. Field
    /// names are explicitly quoted — <see cref="MultipartFormDataContent"/> leaves them bare,
    /// which some OpenAI-compatible multipart parsers reject.
    /// </summary>
    private static MultipartFormDataContent BuildContent(byte[] audioBytes, string language)
    {
        var file = new ByteArrayContent(audioBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = "\"audio.wav\"",
        };

        var content = new MultipartFormDataContent { file };
        AddStringField(content, "response_format", "json");

        if (!string.IsNullOrWhiteSpace(language))
        {
            AddStringField(content, "language", language);
        }

        return content;
    }

    /// <summary>Adds a plain form field with a quoted name.</summary>
    private static void AddStringField(MultipartFormDataContent content, string name, string value)
    {
        var part = new StringContent(value);
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
        content.Add(part);
    }

    /// <summary>Parses the JSON response, tolerating extra fields, with a WAV-length duration fallback.</summary>
    private TranscriptionResult ParseResponse(string json, int audioByteCount, string configuredLanguage)
    {
        TranscriptionResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<TranscriptionResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "The speech service returned an unreadable response.", ex);
        }

        if (response?.Text is null)
        {
            throw new ProviderException(ProviderName, "The speech service response did not contain a transcript.");
        }

        var duration = response.Duration
            ?? Math.Max(0, audioByteCount - WavHeaderBytes) / (double)BytesPerSecond;
        var language = response.Language
            ?? (string.IsNullOrWhiteSpace(configuredLanguage) ? null : configuredLanguage);

        _usageSink.Record(new UsageRecord(
            _timeProvider.GetUtcNow().UtcDateTime,
            UsageCategories.Transcription,
            duration,
            PromptTokens: null,
            CompletionTokens: null));

        _logger.LogDebug("Transcript received: {CharCount} characters, {DurationSeconds:F1} s of audio", response.Text.Length, duration);
        return new TranscriptionResult(response.Text, duration, language);
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

    /// <summary>Wire shape of the transcription response; unknown fields are ignored.</summary>
    private sealed record TranscriptionResponse(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("language")] string? Language);
}
