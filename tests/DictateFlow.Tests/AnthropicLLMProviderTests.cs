using System.Net;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.Anthropic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="AnthropicLLMProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class AnthropicLLMProviderTests
{
    private const string SuccessBody = """
        {
          "content": [{ "type": "text", "text": "enhanced text" }],
          "stop_reason": "end_turn",
          "usage": { "input_tokens": 42, "output_tokens": 17 }
        }
        """;

    private readonly RecordingUsageSink _usageSink = new();

    private readonly AnthropicLlmConfig _config = new()
    {
        ApiKey = "sk-ant-test",
        Model = "claude-opus-4-8",
        Temperature = 0.5,
        MaxTokens = 2000,
        TimeoutSeconds = 60,
    };

    private static PromptContext Context(
        string systemPrompt = "system prompt", string transcript = "user transcript")
        => new(systemPrompt, transcript, Temperature: 0.2, MaxTokens: 1500, ModeName: "Email");

    private IProviderConfigReader CreateConfigReader()
        => new TestProviderConfigReader()
            .Set(ProviderKind.Llm, AnthropicProviders.RegistrationName, _config);

    /// <summary>Creates a provider talking directly to the fake handler (no resilience pipeline).</summary>
    private AnthropicLLMProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateConfigReader(), _usageSink, TimeProvider.System,
            NullLogger<AnthropicLLMProvider>.Instance);

    /// <summary>
    /// Creates a provider through the real DI registration, so requests flow through the
    /// standard resilience pipeline (with near-zero retry delays for test speed).
    /// </summary>
    private AnthropicLLMProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateConfigReader());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddAnthropicLlm(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<AnthropicLLMProvider>();
    }

    [Fact]
    public async Task ProcessAsync_BuildsUrlHeadersAndJsonBody()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri!.ToString());
        Assert.Equal("sk-ant-test", request.Headers["x-api-key"]);
        Assert.Equal(AnthropicLLMProvider.ApiVersion, request.Headers["anthropic-version"]);
        Assert.StartsWith("application/json", request.ContentType);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("claude-opus-4-8", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system prompt", body.RootElement.GetProperty("system").GetString());
        Assert.Equal(1500, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.2, body.RootElement.GetProperty("temperature").GetDouble());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("user transcript", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProcessAsync_ModelRejectsTemperature_RetriesOnceWithoutIt()
    {
        var badRequest = """{"type":"error","error":{"type":"invalid_request_error","message":"`temperature` is not supported on this model."}}""";
        var responses = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            responses++;
            var (status, payload) = responses == 1
                ? (HttpStatusCode.BadRequest, badRequest)
                : (HttpStatusCode.OK, SuccessBody);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            });
        });
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("enhanced text", result);
        Assert.Equal(2, handler.Requests.Count);
        using var first = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.True(first.RootElement.TryGetProperty("temperature", out _)); // first attempt sends it
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.False(second.RootElement.TryGetProperty("temperature", out _)); // retry omits it
    }

    [Fact]
    public async Task ProcessAsync_SuccessResponse_ConcatenatesTextBlocks()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"part one "},{"type":"text","text":"part two"}],"stop_reason":"end_turn"}""");
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("part one part two", result);
    }

    [Fact]
    public async Task ProcessAsync_Success_ReportsTokenUsage()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var record = Assert.Single(_usageSink.Records);
        Assert.Equal(UsageCategories.Llm, record.Category);
        Assert.Equal(42, record.PromptTokens);
        Assert.Equal(17, record.CompletionTokens);
        Assert.Null(record.DurationSeconds);
    }

    [Fact]
    public async Task ProcessAsync_RefusalStopReason_SurfacesActionableError()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"content":[],"stop_reason":"refusal"}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("declined", ex.Message);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task ProcessAsync_BadRequest_SurfacesServiceErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens is too large."}}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("max_tokens is too large", ex.Message);
        Assert.Single(handler.Requests); // no temperature retry for a non-temperature 400
    }

    [Fact]
    public async Task ProcessAsync_UnknownModel_ConfigurationError()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.NotFound,
            """{"type":"error","error":{"type":"not_found_error","message":"model: claude-nope-1"}}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_Unauthorized_ConfigurationErrorWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized);
        var provider = CreateProviderWithResilience(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
        Assert.Single(handler.Requests); // 401 must not be retried
    }

    [Fact]
    public async Task ProcessAsync_CustomEndpoint_RouteAppended()
    {
        _config.Endpoint = "https://my-gateway.example.com";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal(
            "https://my-gateway.example.com/v1/messages",
            handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task ProcessAsync_Timeout_HonorsConfiguredTimeoutSeconds()
    {
        _config.TimeoutSeconds = 1;
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_CallerCancellation_NotWrappedAsProviderException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ProcessAsync(Context(), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_NetworkFailure_ConfigurationErrorWithEndpointHint()
    {
        var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("No such host is known."));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains(_config.Endpoint, ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_MissingApiKey_ConfigurationError()
    {
        _config.ApiKey = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_MissingModel_ConfigurationError()
    {
        _config.Model = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_ResponseWithoutText_ProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"content":[]}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));
        Assert.Empty(_usageSink.Records);
    }
}
