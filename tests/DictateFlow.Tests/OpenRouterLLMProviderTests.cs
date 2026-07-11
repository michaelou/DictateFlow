using System.Net;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.OpenRouter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="OpenRouterLLMProvider"/> against a fake <see cref="HttpMessageHandler"/> —
/// no network involved.
/// </summary>
public sealed class OpenRouterLLMProviderTests
{
    private const string SuccessBody = """
        {
          "choices": [{ "message": { "role": "assistant", "content": "enhanced text" } }],
          "usage": { "prompt_tokens": 42, "completion_tokens": 17, "total_tokens": 59 }
        }
        """;

    private readonly RecordingUsageSink _usageSink = new();

    private readonly OpenRouterLlmConfig _config = new()
    {
        Endpoint = "https://openrouter.ai/api/v1",
        ApiKey = "sk-or-test",
        Model = "openai/gpt-4o-mini",
        Temperature = 0.5,
        MaxTokens = 2000,
        TimeoutSeconds = 60,
    };

    private static PromptContext Context(
        string systemPrompt = "system prompt", string transcript = "user transcript")
        => new(systemPrompt, transcript, Temperature: 0.2, MaxTokens: 1500, ModeName: "Email");

    private IProviderConfigReader CreateConfigReader()
        => new TestProviderConfigReader().Set(ProviderKind.Llm, OpenRouterProviders.RegistrationName, _config);

    private OpenRouterLLMProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateConfigReader(), _usageSink, TimeProvider.System,
            NullLogger<OpenRouterLLMProvider>.Instance);

    private OpenRouterLLMProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateConfigReader());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddOpenRouterLlm(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<OpenRouterLLMProvider>();
    }

    [Fact]
    public async Task ProcessAsync_BuildsUrlBearerHeaderAndJsonBody()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.Uri!.ToString());
        Assert.Equal("Bearer sk-or-test", request.Headers["Authorization"]);
        Assert.StartsWith("application/json", request.ContentType);

        using var body = JsonDocument.Parse(request.Body);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system prompt", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("user transcript", messages[1].GetProperty("content").GetString());
        Assert.Equal(0.2, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(1500, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("openai/gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ProcessAsync_FullChatCompletionsUrl_UsedAsIs()
    {
        _config.Endpoint = "https://gateway.example.com/v1/chat/completions";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("https://gateway.example.com/v1/chat/completions", handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task ProcessAsync_ModelRejectsTemperature_RetriesOnceWithoutIt()
    {
        var badRequest = """{"error":{"message":"temperature is not supported with this model."}}""";
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
        Assert.True(first.RootElement.TryGetProperty("temperature", out _));
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.False(second.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task ProcessAsync_SuccessResponse_ReturnsContentAndReportsUsage()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("enhanced text", result);
        var record = Assert.Single(_usageSink.Records);
        Assert.Equal(UsageCategories.Llm, record.Category);
        Assert.Equal(42, record.PromptTokens);
        Assert.Equal(17, record.CompletionTokens);
    }

    [Fact]
    public async Task ProcessAsync_ErrorObjectInSuccessBody_SurfacedAsProviderException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"error":{"message":"No endpoints found for model."}}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("No endpoints found", ex.Message);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task ProcessAsync_BadRequest_SurfacesServiceErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.BadRequest, """{"error":{"message":"max_tokens too large."}}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("max_tokens too large", ex.Message);
        Assert.Single(handler.Requests); // no temperature retry for a non-temperature 400
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
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProcessAsync_TooManyRequests_RetriedThenSurfaced()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests);
        var provider = CreateProviderWithResilience(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.False(ex.IsConfigurationError);
        Assert.Contains("429", ex.Message);
        Assert.Equal(4, handler.Requests.Count);
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
    public async Task ProcessAsync_InvalidEndpoint_ConfigurationError()
    {
        _config.Endpoint = "not a url";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
    }
}
