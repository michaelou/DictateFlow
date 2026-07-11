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

namespace DictateFlow.Providers.OpenRouter;

/// <summary>
/// <see cref="ILLMProvider"/> backed by <see href="https://openrouter.ai">OpenRouter</see>
/// through its OpenAI-compatible chat-completions surface
/// (<c>POST /api/v1/chat/completions</c>). Reads its <see cref="OpenRouterLlmConfig"/> section
/// on every call, maps all transport failures to <see cref="ProviderException"/>, reports token
/// usage to <see cref="IUsageSink"/> after each successful call, and never logs the API key or
/// the text above Debug.
/// </summary>
public sealed class OpenRouterLLMProvider : ILLMProvider
{
    private const string ProviderName = "OpenRouterLLM";

    /// <summary>Optional attribution headers OpenRouter uses for app ranking; harmless to send.</summary>
    private const string RefererHeaderValue = "https://github.com/michaelou/DictateFlow";
    private const string TitleHeaderValue = "DictateFlow";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenRouterLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenRouterLLMProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the endpoint, key, model and timeout, read per call.</param>
    /// <param name="usageSink">Receives token usage after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public OpenRouterLLMProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<OpenRouterLLMProvider> logger)
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
        var llm = _configReader.GetConfig<OpenRouterLlmConfig>(ProviderKind.Llm, OpenRouterProviders.RegistrationName);
        var requestUri = OpenRouterEndpoint.BuildChatCompletionsUri(llm.Endpoint, ProviderName);

        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your OpenRouter API key in Settings → LLM.",
                isConfigurationError: true);
        }

        if (string.IsNullOrWhiteSpace(llm.Model))
        {
            throw new ProviderException(
                ProviderName, "No model is configured. Enter a model slug (e.g. openai/gpt-4o-mini) in Settings → LLM.",
                isConfigurationError: true);
        }

        var messages = new[]
        {
            new ChatMessage("system", context.SystemPrompt),
            new ChatMessage("user", context.Transcript),
        };

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
            // Some reasoning models reject a non-default 'temperature'. When the service says
            // so, retry once without it so those models work while models that honour it still
            // receive the mode's temperature.
            var includeTemperature = true;
            while (true)
            {
                var requestBody = JsonSerializer.Serialize(new ChatRequest(
                    llm.Model.Trim(), messages, includeTemperature ? context.Temperature : null, context.MaxTokens));

                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {llm.ApiKey.Trim()}");
                request.Headers.TryAddWithoutValidation("HTTP-Referer", RefererHeaderValue);
                request.Headers.TryAddWithoutValidation("X-Title", TitleHeaderValue);

                using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                _logger.LogDebug(
                    "OpenRouter chat completion request finished: HTTP {StatusCode} in {ElapsedMs} ms (mode '{ModeName}')",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, context.ModeName);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new ProviderException(
                        ProviderName,
                        $"OpenRouter rejected the request ({(int)response.StatusCode}). Check your API key in Settings → LLM.",
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

                    throw new ProviderException(
                        ProviderName,
                        $"OpenRouter returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{OpenRouterError.Describe(errorBody)} Check the model slug and options in Settings → LLM.");
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
                $"Could not reach OpenRouter at '{llm.Endpoint}' ({ex.Message}). Check your internet connection and the endpoint in Settings → LLM.",
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
    /// Parses the JSON response, tolerating extra fields, and reports token usage to the usage
    /// sink when the service supplied it. An OpenRouter <c>error</c> object in a 200 body is
    /// surfaced instead of empty output.
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
            throw new ProviderException(ProviderName, "OpenRouter returned an unreadable response.", ex);
        }

        if (response?.Error is { Message: { Length: > 0 } errorMessage })
        {
            throw new ProviderException(ProviderName, $"OpenRouter reported an error: {errorMessage}");
        }

        var content = response?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrEmpty(content))
        {
            throw new ProviderException(ProviderName, "The OpenRouter response did not contain any text.");
        }

        var usage = response!.Usage;
        if (usage is not null)
        {
            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.Llm,
                DurationSeconds: null,
                usage.PromptTokens,
                usage.CompletionTokens));
        }

        _logger.LogDebug(
            "Enhanced text received: {CharCount} characters, {PromptTokens} prompt + {CompletionTokens} completion tokens",
            content.Length, usage?.PromptTokens, usage?.CompletionTokens);
        return content;
    }

    /// <summary>
    /// Wire shape of the chat-completions request. Temperature is omitted when null (reasoning
    /// models reject a non-default value). <c>max_tokens</c> is the limit OpenRouter accepts
    /// across models.
    /// </summary>
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    /// <summary>One chat message.</summary>
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    /// <summary>Wire shape of the chat-completions response; unknown fields are ignored.</summary>
    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage,
        [property: JsonPropertyName("error")] ChatError? Error);

    /// <summary>One completion choice.</summary>
    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message);

    /// <summary>The assistant message of a choice.</summary>
    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);

    /// <summary>Token usage block.</summary>
    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);

    /// <summary>An error object OpenRouter may return even with a 200 status.</summary>
    private sealed record ChatError(
        [property: JsonPropertyName("message")] string? Message);
}
