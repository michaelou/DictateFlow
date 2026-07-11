using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.OpenRouter;

/// <summary>
/// <see cref="ITranscriptionProvider"/> backed by <see href="https://openrouter.ai">OpenRouter</see>.
/// OpenRouter has no dedicated speech-to-text endpoint, so the recording is sent as multimodal
/// audio input to an audio-capable chat model through the OpenAI-compatible chat-completions
/// surface, with an instruction to transcribe verbatim. Reads its
/// <see cref="OpenRouterTranscriptionConfig"/> section on every call, maps all transport
/// failures to <see cref="ProviderException"/>, reports the audio duration to
/// <see cref="IUsageSink"/> after each successful call, and never logs the API key or the
/// transcript above Debug.
/// </summary>
public sealed class OpenRouterTranscriptionProvider : ITranscriptionProvider
{
    private const string ProviderName = OpenRouterProviders.RegistrationName;

    /// <summary>Optional attribution headers OpenRouter uses for app ranking; harmless to send.</summary>
    private const string RefererHeaderValue = "https://github.com/michaelou/DictateFlow";
    private const string TitleHeaderValue = "DictateFlow";

    /// <summary>Size of the WAV header; subtracted when computing duration from byte length.</summary>
    private const int WavHeaderBytes = 44;

    /// <summary>Bytes of PCM data per second for 16 kHz × 16-bit × mono audio.</summary>
    private const int BytesPerSecond = 16000 * 2;

    /// <summary>Completion-token ceiling for a transcript; generous for a single utterance.</summary>
    private const int MaxTranscriptTokens = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenRouterTranscriptionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenRouterTranscriptionProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the endpoint, key, model, language and timeout, read per call.</param>
    /// <param name="usageSink">Receives the audio duration after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OpenRouterTranscriptionProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<OpenRouterTranscriptionProvider> logger)
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
        var speech = _configReader.GetConfig<OpenRouterTranscriptionConfig>(
            ProviderKind.Transcription, OpenRouterProviders.RegistrationName);
        var requestUri = OpenRouterEndpoint.BuildChatCompletionsUri(speech.Endpoint, ProviderName);

        if (string.IsNullOrWhiteSpace(speech.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your OpenRouter API key in Settings → Speech.",
                isConfigurationError: true);
        }

        if (string.IsNullOrWhiteSpace(speech.Model))
        {
            throw new ProviderException(
                ProviderName,
                "No model is configured. Enter an audio-capable model slug (e.g. google/gemini-2.5-flash) in Settings → Speech.",
                isConfigurationError: true);
        }

        // Buffer the audio so the resilience pipeline can resend the body on retries.
        var audioBytes = await ReadAllBytesAsync(audio, cancellationToken).ConfigureAwait(false);
        var audioBase64 = Convert.ToBase64String(audioBytes);

        // The user-facing timeout: enforced here (instead of at pipeline-build time) so a
        // changed TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (speech.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(speech.TimeoutSeconds));
        }

        var requestBody = JsonSerializer.Serialize(new ChatRequest(
            speech.Model.Trim(),
            [new ChatMessage("user", [ContentPart.FromText(BuildInstruction(speech.Language)), ContentPart.FromAudio(audioBase64)])],
            MaxTranscriptTokens));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {speech.ApiKey.Trim()}");
            request.Headers.TryAddWithoutValidation("HTTP-Referer", RefererHeaderValue);
            request.Headers.TryAddWithoutValidation("X-Title", TitleHeaderValue);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug(
                "OpenRouter transcription request finished: HTTP {StatusCode} in {ElapsedMs} ms for {AudioBytes} audio bytes",
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds, audioBytes.Length);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    ProviderName,
                    $"OpenRouter rejected the request ({(int)response.StatusCode}). Check your API key in Settings → Speech.",
                    isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                throw new ProviderException(
                    ProviderName,
                    $"OpenRouter returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{OpenRouterError.Describe(errorBody)} The model must be a multimodal chat model that accepts audio input (e.g. google/gemini-2.5-flash or openai/gpt-4o-audio-preview) — transcription-only models such as Whisper are not usable through OpenRouter. Check the model slug in Settings → Speech.");
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
                $"Could not reach OpenRouter at '{speech.Endpoint}' ({ex.Message}). Check your internet connection and the endpoint in Settings → Speech.",
                ex,
                isConfigurationError: true);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Transcription failed: {ex.Message}", ex);
        }
    }

    /// <summary>Builds the transcription instruction, adding the configured language hint when set.</summary>
    private static string BuildInstruction(string language)
    {
        var instruction =
            "Transcribe the following audio verbatim. Output only the spoken words with no commentary, "
            + "labels, quotation marks or timestamps. If there is no speech, output nothing.";
        return string.IsNullOrWhiteSpace(language)
            ? instruction
            : $"{instruction} The spoken language is {language.Trim()}.";
    }

    /// <summary>
    /// Parses the chat-completions response, tolerating extra fields, and reports the audio
    /// duration (computed from the WAV length) to the usage sink. An empty transcript (e.g.
    /// silence) is valid and does not fail.
    /// </summary>
    private TranscriptionResult ParseResponse(string json, int audioByteCount, string configuredLanguage)
    {
        ChatResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ChatResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "OpenRouter returned an unreadable response.", ex);
        }

        if (response?.Error is { Message: { Length: > 0 } errorMessage })
        {
            throw new ProviderException(ProviderName, $"OpenRouter reported an error: {errorMessage}");
        }

        // A valid response carries a choice; its content may legitimately be an empty string
        // (silence). A reply with no choice at all is unrecognized.
        if (response?.Choices is not { Count: > 0 } choices)
        {
            throw new ProviderException(ProviderName, "The OpenRouter response did not contain a transcript.");
        }

        var text = (choices[0].Message?.Content ?? "").Trim();
        var duration = Math.Max(0, audioByteCount - WavHeaderBytes) / (double)BytesPerSecond;
        var language = string.IsNullOrWhiteSpace(configuredLanguage) ? null : configuredLanguage.Trim();

        _usageSink.Record(new UsageRecord(
            _timeProvider.GetUtcNow().UtcDateTime,
            UsageCategories.Speech,
            duration,
            PromptTokens: null,
            CompletionTokens: null));

        _logger.LogDebug("Transcript received: {CharCount} characters, {DurationSeconds:F1} s of audio", text.Length, duration);
        return new TranscriptionResult(text, duration, language);
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

    /// <summary>Wire shape of the chat-completions request; one user message with mixed content parts.</summary>
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    /// <summary>One chat message whose content is an ordered list of parts.</summary>
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<ContentPart> Content);

    /// <summary>
    /// One multimodal content part: a <c>text</c> part carries <see cref="Text"/>; an
    /// <c>input_audio</c> part carries <see cref="InputAudio"/>. The unused field is omitted.
    /// </summary>
    private sealed record ContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("input_audio"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InputAudio? InputAudio)
    {
        public static ContentPart FromText(string text) => new("text", text, null);

        public static ContentPart FromAudio(string base64) => new("input_audio", null, new InputAudio(base64, "wav"));
    }

    /// <summary>Base64-encoded audio payload and its container format.</summary>
    private sealed record InputAudio(
        [property: JsonPropertyName("data")] string Data,
        [property: JsonPropertyName("format")] string Format);

    /// <summary>Wire shape of the chat-completions response; unknown fields are ignored.</summary>
    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
        [property: JsonPropertyName("error")] ChatError? Error);

    /// <summary>One completion choice.</summary>
    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message);

    /// <summary>The assistant message of a choice.</summary>
    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    /// <summary>An error object OpenRouter may return even with a 200 status.</summary>
    private sealed record ChatError(
        [property: JsonPropertyName("message")] string? Message);
}
