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

namespace DictateFlow.Providers.Anthropic;

/// <summary>
/// <see cref="ILLMProvider"/> backed by the Anthropic Messages API
/// (<c>POST /v1/messages</c>). Reads its <see cref="AnthropicLlmConfig"/> section on every
/// call, maps all transport failures to <see cref="ProviderException"/>, reports token usage
/// to <see cref="IUsageSink"/> after each successful call, and never logs the API key or the
/// text above Debug.
/// </summary>
public sealed class AnthropicLLMProvider : ILLMProvider
{
    /// <summary>The API version header value the Messages API requires.</summary>
    public const string ApiVersion = "2023-06-01";

    private const string ProviderName = "AnthropicLLM";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnthropicLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AnthropicLLMProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the key, model and timeout, read per call.</param>
    /// <param name="usageSink">Receives token usage after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AnthropicLLMProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AnthropicLLMProvider> logger)
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
        var llm = _configReader.GetConfig<AnthropicLlmConfig>(ProviderKind.Llm, AnthropicProviders.RegistrationName);
        var requestUri = BuildRequestUri(llm);

        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your Anthropic API key in Settings → LLM.",
                isConfigurationError: true);
        }

        if (string.IsNullOrWhiteSpace(llm.Model))
        {
            throw new ProviderException(
                ProviderName, "No model is configured. Enter a model id (e.g. claude-opus-4-8) in Settings → LLM.",
                isConfigurationError: true);
        }

        // The user-facing timeout: enforced here (instead of at pipeline-build time) so a
        // changed TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (llm.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Current Anthropic models (Opus 4.7+, Sonnet 5, Fable 5) reject a non-default
            // 'temperature' with a 400. When the service says so, retry once without it so
            // those models work while older models still receive the mode's temperature.
            var includeTemperature = true;
            while (true)
            {
                var requestBody = JsonSerializer.Serialize(new MessagesRequest(
                    llm.Model.Trim(),
                    context.MaxTokens,
                    context.SystemPrompt,
                    [new MessageParam("user", context.Transcript)],
                    includeTemperature ? context.Temperature : null));

                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("x-api-key", llm.ApiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);

                using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                _logger.LogDebug(
                    "Anthropic messages request finished: HTTP {StatusCode} in {ElapsedMs} ms (mode '{ModeName}')",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, context.ModeName);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new ProviderException(
                        ProviderName,
                        $"The Anthropic API rejected the request ({(int)response.StatusCode}). Check your API key in Settings → LLM.",
                        isConfigurationError: true);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.BadRequest
                        && includeTemperature
                        && errorBody.Contains("temperature", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Model rejected 'temperature'; retrying once without it");
                        includeTemperature = false;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound
                        && errorBody.Contains("model", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ProviderException(
                            ProviderName,
                            $"The Anthropic API does not know the model '{llm.Model}'.{DescribeError(errorBody)} Check the model id in Settings → LLM.",
                            isConfigurationError: true);
                    }

                    throw new ProviderException(
                        ProviderName,
                        $"The Anthropic API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{DescribeError(errorBody)} Check the model and options in Settings → LLM.");
                }

                stopwatch.Stop();
                var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                return ParseResponse(json);
            }
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
                $"The enhancement request timed out after {llm.TimeoutSeconds} s. Try again, or raise the timeout in Settings → LLM.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Could not reach the Anthropic API at '{llm.Endpoint}' ({ex.Message}). Check your internet connection and the endpoint in Settings → LLM.",
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
    /// Builds the Messages API request URI from the configured base URL, accepting either the
    /// bare host (the route is appended) or a complete <c>…/v1/messages</c> URL.
    /// </summary>
    private static Uri BuildRequestUri(AnthropicLlmConfig llm)
    {
        var endpointText = llm.Endpoint.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderException(
                ProviderName,
                $"'{llm.Endpoint}' is not a valid http(s) URL. Check the endpoint in Settings → LLM.",
                isConfigurationError: true);
        }

        return endpoint.AbsolutePath.TrimEnd('/').EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : new Uri($"{endpointText.TrimEnd('/')}/v1/messages");
    }

    /// <summary>
    /// Extracts a short, human-readable detail from an error response body — the service's
    /// <c>error.message</c> when present, otherwise a trimmed snippet — prefixed with a space
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
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return $" {message.GetString()}";
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
    /// usage sink when the service supplied it. A <c>refusal</c> stop reason becomes a
    /// user-visible error instead of empty output.
    /// </summary>
    private string ParseResponse(string json)
    {
        MessagesResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<MessagesResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderName, "The Anthropic API returned an unreadable response.", ex);
        }

        if (string.Equals(response?.StopReason, "refusal", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProviderException(
                ProviderName, "The model declined to process this dictation (safety refusal). Try rephrasing it.");
        }

        var content = response?.Content is { } blocks
            ? string.Concat(blocks
                .Where(b => string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                .Select(b => b.Text))
            : null;
        if (string.IsNullOrEmpty(content))
        {
            throw new ProviderException(ProviderName, "The Anthropic API response did not contain any text.");
        }

        var usage = response!.Usage;
        if (usage is not null)
        {
            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.Llm,
                DurationSeconds: null,
                usage.InputTokens,
                usage.OutputTokens));
        }

        _logger.LogDebug(
            "Enhanced text received: {CharCount} characters, {InputTokens} input + {OutputTokens} output tokens",
            content.Length, usage?.InputTokens, usage?.OutputTokens);
        return content;
    }

    /// <summary>
    /// Wire shape of the Messages API request. The temperature is omitted when null
    /// (current models reject a non-default value).
    /// </summary>
    private sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<MessageParam> Messages,
        [property: JsonPropertyName("temperature"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Temperature);

    /// <summary>One chat message.</summary>
    private sealed record MessageParam(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    /// <summary>Wire shape of the Messages API response; unknown fields are ignored.</summary>
    private sealed record MessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason,
        [property: JsonPropertyName("usage")] MessagesUsage? Usage);

    /// <summary>One response content block; only text blocks are consumed.</summary>
    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);

    /// <summary>Token usage block.</summary>
    private sealed record MessagesUsage(
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens);
}
