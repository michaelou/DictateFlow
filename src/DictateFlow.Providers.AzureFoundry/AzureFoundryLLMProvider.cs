using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Providers.AzureFoundry;

/// <summary>
/// <see cref="ILLMProvider"/> backed by an Azure AI Foundry deployment through the
/// OpenAI-compatible chat-completions surface. Reads <see cref="LlmSettings"/> on every call,
/// maps all transport failures to <see cref="ProviderException"/>, reports token usage to
/// <see cref="IUsageSink"/> after each successful call, and never logs the API key or the
/// text above Debug.
/// </summary>
public sealed class AzureFoundryLLMProvider : ILLMProvider
{
    /// <summary>API version sent when the endpoint does not already specify one.</summary>
    public const string DefaultApiVersion = "2024-06-01";

    private const string ProviderName = "AzureFoundryLLM";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureFoundryLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureFoundryLLMProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="settingsService">Supplies the LLM endpoint, key, deployment and timeout.</param>
    /// <param name="usageSink">Receives token usage after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureFoundryLLMProvider(
        HttpClient httpClient,
        ISettingsService settingsService,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AzureFoundryLLMProvider> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _usageSink = usageSink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
    {
        var llm = _settingsService.Current.Llm;
        var requestUri = BuildRequestUri(llm);

        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your Azure AI Foundry API key in Settings → LLM.",
                isConfigurationError: true);
        }

        var requestBody = JsonSerializer.Serialize(new ChatRequest(
            [new ChatMessage("system", context.SystemPrompt), new ChatMessage("user", context.Transcript)],
            context.Temperature,
            context.MaxTokens));

        // The user-facing timeout: enforced here (instead of at pipeline-build time) so a
        // changed Llm.TimeoutSeconds takes effect immediately, without a restart.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (llm.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("api-key", llm.ApiKey);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug(
                "Chat completion request finished: HTTP {StatusCode} in {ElapsedMs} ms (mode '{ModeName}')",
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds, context.ModeName);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    ProviderName,
                    $"The LLM service rejected the request ({(int)response.StatusCode}). Check your API key in Settings → LLM.",
                    isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderName,
                    $"The LLM service returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Try again; if it persists, check the endpoint and deployment name in Settings → LLM.");
            }

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
                $"The enhancement request timed out after {llm.TimeoutSeconds} s. Try again, or raise the timeout in Settings → LLM.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(
                ProviderName,
                $"Could not reach the LLM endpoint '{llm.Endpoint}' ({ex.Message}). Check the endpoint URL in Settings → LLM.",
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
    /// Builds the request URI. When the configured endpoint is just a host, the standard
    /// deployments route is appended; when the user pasted a complete target URI (a path is
    /// already present), it is used as-is and only the api-version query is added if missing.
    /// </summary>
    private Uri BuildRequestUri(LlmSettings llm)
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

        if (endpoint.AbsolutePath.Length > 1)
        {
            _logger.LogDebug("LLM endpoint contains a path; using it as the full target URI");
            if (endpoint.Query.Contains("api-version", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            return new Uri($"{endpoint.AbsoluteUri}{separator}api-version={DefaultApiVersion}");
        }

        if (string.IsNullOrWhiteSpace(llm.DeploymentName))
        {
            throw new ProviderException(
                ProviderName,
                "No deployment name is configured. Enter the model deployment name in Settings → LLM.",
                isConfigurationError: true);
        }

        _logger.LogDebug("LLM endpoint is a bare host; building the deployments route");
        var baseUrl = endpointText.TrimEnd('/');
        return new Uri(
            $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(llm.DeploymentName.Trim())}/chat/completions?api-version={DefaultApiVersion}");
    }

    /// <summary>
    /// Parses the JSON response, tolerating extra fields, and reports token usage to the
    /// usage sink when the service supplied it.
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
            throw new ProviderException(ProviderName, "The LLM service returned an unreadable response.", ex);
        }

        var content = response?.Choices?.FirstOrDefault()?.Message?.Content;
        if (content is null)
        {
            throw new ProviderException(ProviderName, "The LLM service response did not contain any text.");
        }

        var usage = response!.Usage;
        if (usage is not null)
        {
            _usageSink.Record(new UsageRecord(
                _timeProvider.GetUtcNow().UtcDateTime,
                UsageCategories.LlmEnhancement,
                DurationSeconds: null,
                usage.PromptTokens,
                usage.CompletionTokens));
        }

        _logger.LogDebug(
            "Enhanced text received: {CharCount} characters, {PromptTokens} prompt + {CompletionTokens} completion tokens",
            content.Length, usage?.PromptTokens, usage?.CompletionTokens);
        return content;
    }

    /// <summary>Wire shape of the chat-completions request.</summary>
    private sealed record ChatRequest(
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    /// <summary>One chat message.</summary>
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    /// <summary>Wire shape of the chat-completions response; unknown fields are ignored.</summary>
    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

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
}
