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

namespace DictateFlow.Providers.AzureFoundry;

/// <summary>
/// <see cref="ILLMProvider"/> backed by an Azure AI Foundry deployment through the
/// OpenAI-compatible chat-completions surface. Reads its <see cref="AzureFoundryLlmConfig"/>
/// section on every call, maps all transport failures to <see cref="ProviderException"/>,
/// reports token usage to <see cref="IUsageSink"/> after each successful call, and never logs
/// the API key or the text above Debug.
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
    private readonly IProviderConfigReader _configReader;
    private readonly IUsageSink _usageSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureFoundryLLMProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AzureFoundryLLMProvider"/> class.</summary>
    /// <param name="httpClient">Client supplied by <c>IHttpClientFactory</c>, wrapped in the standard resilience pipeline.</param>
    /// <param name="configReader">Supplies the endpoint, key, deployment and timeout, read per call.</param>
    /// <param name="usageSink">Receives token usage after each successful call.</param>
    /// <param name="timeProvider">Timestamps usage records (replaceable in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public AzureFoundryLLMProvider(
        HttpClient httpClient,
        IProviderConfigReader configReader,
        IUsageSink usageSink,
        TimeProvider timeProvider,
        ILogger<AzureFoundryLLMProvider> logger)
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
        var llm = _configReader.GetConfig<AzureFoundryLlmConfig>(ProviderKind.Llm, AzureFoundryProviders.RegistrationName);
        var requestUri = BuildRequestUri(llm);

        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new ProviderException(
                ProviderName, "No API key is configured. Enter your Azure AI Foundry API key in Settings → LLM.",
                isConfigurationError: true);
        }

        // The deployment/model name travels in the body. The classic Azure route ignores it
        // (the deployment is in the URL); the OpenAI v1 surface requires it.
        var model = string.IsNullOrWhiteSpace(llm.DeploymentName) ? null : llm.DeploymentName.Trim();
        var messages = new[]
        {
            new ChatMessage("system", context.SystemPrompt),
            new ChatMessage("user", context.Transcript),
        };

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
            // The GPT-5 reasoning series (and similar) reject a non-default 'temperature'. When
            // the service says so, retry once without it so those models work while models that
            // do honour temperature (e.g. gpt-4o) still receive it.
            var includeTemperature = true;
            while (true)
            {
                var requestBody = JsonSerializer.Serialize(new ChatRequest(
                    model, messages, includeTemperature ? context.Temperature : null, context.MaxTokens));

                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("api-key", llm.ApiKey);

                using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
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
                        $"The LLM service returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{DescribeError(errorBody)} Check the endpoint, deployment name and options in Settings → LLM.");
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
    /// Builds the chat-completions request URI, supporting the three shapes users paste:
    /// a complete <c>…/chat/completions</c> URL (used as-is; the classic deployments route also
    /// gets an api-version), the OpenAI v1 base <c>…/openai/v1</c> (the route is appended and the
    /// model travels in the body), and a bare host (the classic Azure deployments route is built).
    /// </summary>
    private Uri BuildRequestUri(AzureFoundryLlmConfig llm)
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

        var path = endpoint.AbsolutePath.TrimEnd('/');

        // A complete chat-completions URL was pasted: use it as-is. Only the classic
        // "/deployments/…" route carries an api-version; the v1 surface does not.
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var isClassic = path.Contains("/deployments/", StringComparison.OrdinalIgnoreCase);
            if (!isClassic)
            {
                RequireDeployment(llm);
            }

            if (isClassic && !endpoint.Query.Contains("api-version", StringComparison.OrdinalIgnoreCase))
            {
                var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
                return new Uri($"{endpoint.AbsoluteUri}{separator}api-version={DefaultApiVersion}");
            }

            return endpoint;
        }

        // The OpenAI v1 base (…/openai/v1): append the route; the model goes in the body.
        if (path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            RequireDeployment(llm);
            _logger.LogDebug("LLM endpoint is the OpenAI v1 base; appending the chat/completions route");
            return new Uri($"{endpointText.TrimEnd('/')}/chat/completions");
        }

        // A bare host: build the classic Azure OpenAI deployments route.
        if (endpoint.AbsolutePath.Length <= 1)
        {
            RequireDeployment(llm);
            _logger.LogDebug("LLM endpoint is a bare host; building the deployments route");
            var baseUrl = endpointText.TrimEnd('/');
            return new Uri(
                $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(llm.DeploymentName.Trim())}/chat/completions?api-version={DefaultApiVersion}");
        }

        // Some other custom path: use it as-is, appending api-version if missing.
        _logger.LogDebug("LLM endpoint contains a custom path; using it as the full target URI");
        if (endpoint.Query.Contains("api-version", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var sep = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        return new Uri($"{endpoint.AbsoluteUri}{sep}api-version={DefaultApiVersion}");
    }

    /// <summary>Throws a configuration error when no deployment/model name is set.</summary>
    private void RequireDeployment(AzureFoundryLlmConfig llm)
    {
        if (string.IsNullOrWhiteSpace(llm.DeploymentName))
        {
            throw new ProviderException(
                ProviderName,
                "No deployment name is configured. Enter the model deployment name in Settings → LLM.",
                isConfigurationError: true);
        }
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
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return $" {message.GetString()}";
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return $" {error.GetString()}";
                }
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
    /// Wire shape of the chat-completions request. The model and temperature are omitted when
    /// null (the v1 surface requires the model in the body; reasoning models reject temperature).
    /// <c>max_completion_tokens</c> is the modern token limit accepted across current models.
    /// </summary>
    private sealed record ChatRequest(
        [property: JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Temperature,
        [property: JsonPropertyName("max_completion_tokens")] int MaxTokens);

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
