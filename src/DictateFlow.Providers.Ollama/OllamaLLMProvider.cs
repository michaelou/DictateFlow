using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Providers;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.Ollama;

/// <summary>
/// <see cref="ILLMProvider"/> backed by an <see href="https://ollama.com">Ollama</see> server
/// through its native chat API (<c>POST /api/chat</c>) — a local daemon by default, or
/// Ollama Cloud / any remote host when a base URL and API key are configured. Reads its
/// <see cref="OllamaLlmConfig"/> section on every call, maps all transport failures to
/// <see cref="ProviderException"/>, reports token usage to <see cref="IUsageSink"/> after
/// each successful call, and never logs the API key or the text above Debug.
/// </summary>
public sealed class OllamaLLMProvider : ILLMProvider
{
    private const string ProviderName = "OllamaLLM";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OllamaLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="OllamaLLMProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the base URL, key, model and timeout, read per call.</param>
    /// <param name="usageSink">Receives token usage after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OllamaLLMProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<OllamaLLMProvider> logger)
    {
        _httpClient = httpClient;
        _configReader = configReader;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
    {
        var llm = _configReader.GetConfig<OllamaLlmConfig>(ProviderKind.Llm, OllamaProviders.RegistrationName);
        var requestUri = BuildRequestUri(llm);

        if (string.IsNullOrWhiteSpace(llm.Model))
        {
            throw new ProviderException(
                ProviderName, "No model is configured. Enter a model name (e.g. llama3.2) in Settings → LLM.",
                isConfigurationError: true);
        }

        // The user-facing timeout: enforced here (instead of at pipeline-build time) so a
        // changed TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (llm.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        }

        var requestBody = JsonSerializer.Serialize(new ChatRequest(
            llm.Model.Trim(),
            [
                new ChatMessage("system", context.SystemPrompt),
                new ChatMessage("user", context.Transcript),
            ],
            Stream: false,
            new ChatOptions(context.Temperature, context.MaxTokens)));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(llm.ApiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {llm.ApiKey.Trim()}");
            }

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            _logger.LogDebug(
                "Ollama chat request finished: HTTP {StatusCode} in {ElapsedMs} ms (mode '{ModeName}')",
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds, context.ModeName);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    ProviderName,
                    $"The Ollama server rejected the request ({(int)response.StatusCode}). Check your API key in Settings → LLM.",
                    isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound
                    && errorBody.Contains("model", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProviderException(
                        ProviderName,
                        $"The Ollama server does not have the model '{llm.Model}'.{DescribeError(errorBody)} Pull it first (ollama pull {llm.Model}) or pick another model in Settings → LLM.",
                        isConfigurationError: true);
                }

                throw new ProviderException(
                    ProviderName,
                    $"The Ollama server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{DescribeError(errorBody)} Check the base URL and model in Settings → LLM.");
            }

            stopwatch.Stop();
            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseResponse(json);
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
                $"The enhancement request timed out after {llm.TimeoutSeconds} s. Try a smaller model, or raise the timeout in Settings → LLM.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Could not reach the Ollama server at '{llm.BaseUrl}' ({ex.Message}). Is Ollama running? Check the base URL in Settings → LLM.",
                ex,
                isConfigurationError: true);
        }
        catch (Exception ex)
        {
            // Defensive: nothing rawer than ProviderException may escape the provider.
            throw new ProviderException(ProviderName, $"Enhancement failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds the chat request URI from the configured base URL, accepting either the bare
    /// host (the route is appended) or a complete <c>…/api/chat</c> URL.
    /// </summary>
    private static Uri BuildRequestUri(OllamaLlmConfig llm)
    {
        var baseUrlText = llm.BaseUrl.Trim();
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderException(
                ProviderName,
                $"'{llm.BaseUrl}' is not a valid http(s) URL. Check the base URL in Settings → LLM.",
                isConfigurationError: true);
        }

        return baseUrl.AbsolutePath.TrimEnd('/').EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : new Uri($"{baseUrlText.TrimEnd('/')}/api/chat");
    }

    /// <summary>
    /// Extracts a short, human-readable detail from an error response body — Ollama's
    /// <c>error</c> string when present, otherwise a trimmed snippet — prefixed with a space
    /// so it reads cleanly appended to the status line (empty when there is nothing useful).
    /// </summary>
    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return $" {error.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw snippet.
        }

        var trimmed = body.Trim();
        return trimmed.Length > 300 ? $" {trimmed[..300]}…" : $" {trimmed}";
    }

    /// <summary>
    /// Parses the JSON response, tolerating extra fields, and reports token usage to the
    /// usage sink when the server supplied it.
    /// </summary>
    private string ParseResponse(string json)
    {
        ChatResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ChatResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "The Ollama server returned an unreadable response.", ex);
        }

        var content = response?.Message?.Content;
        if (string.IsNullOrEmpty(content))
        {
            throw new ProviderException(ProviderName, "The Ollama response did not contain any text.");
        }

        if (response!.PromptEvalCount is not null || response.EvalCount is not null)
        {
            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.Llm,
                DurationSeconds: null,
                response.PromptEvalCount,
                response.EvalCount));
        }

        _logger.LogDebug(
            "Enhanced text received: {CharCount} characters, {PromptTokens} prompt + {CompletionTokens} completion tokens",
            content.Length, response.PromptEvalCount, response.EvalCount);
        return content;
    }

    /// <summary>Wire shape of the Ollama chat request. Streaming is disabled — one JSON reply.</summary>
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] ChatOptions Options);

    /// <summary>One chat message.</summary>
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    /// <summary>Model options; <c>num_predict</c> is Ollama's completion-token limit.</summary>
    private sealed record ChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    /// <summary>Wire shape of the Ollama chat response; unknown fields are ignored.</summary>
    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    /// <summary>The assistant message of the response.</summary>
    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);
}
