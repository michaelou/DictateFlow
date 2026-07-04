using System.Net;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="OllamaLLMProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class OllamaLLMProviderTests
{
    private const string SuccessBody = """
        {
          "model": "llama3.2",
          "message": { "role": "assistant", "content": "enhanced text" },
          "done": true,
          "prompt_eval_count": 42,
          "eval_count": 17
        }
        """;

    private readonly RecordingUsageSink _usageSink = new();

    private readonly OllamaLlmConfig _config = new()
    {
        BaseUrl = "http://localhost:11434",
        ApiKey = "",
        Model = "llama3.2",
        Temperature = 0.5,
        MaxTokens = 2000,
        TimeoutSeconds = 120,
    };

    private static PromptContext Context(
        string systemPrompt = "system prompt", string transcript = "user transcript")
        => new(systemPrompt, transcript, Temperature: 0.2, MaxTokens: 1500, ModeName: "Email");

    private IProviderConfigReader CreateConfigReader()
        => new TestProviderConfigReader()
            .Set(ProviderKind.Llm, OllamaProviders.RegistrationName, _config);

    /// <summary>Creates a provider talking directly to the fake handler (no resilience pipeline).</summary>
    private OllamaLLMProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateConfigReader(), _usageSink, TimeProvider.System,
            NullLogger<OllamaLLMProvider>.Instance);

    /// <summary>
    /// Creates a provider through the real DI registration, so requests flow through the
    /// standard resilience pipeline (with near-zero retry delays for test speed).
    /// </summary>
    private OllamaLLMProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateConfigReader());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddOllamaLlm(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<OllamaLLMProvider>();
    }

    [Fact]
    public async Task ProcessAsync_BuildsUrlAndJsonBody_NoAuthHeaderWithoutKey()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/api/chat", request.Uri!.ToString());
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.StartsWith("application/json", request.ContentType);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("llama3.2", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("system prompt", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("user transcript", messages[1].GetProperty("content").GetString());
        var options = body.RootElement.GetProperty("options");
        Assert.Equal(0.2, options.GetProperty("temperature").GetDouble());
        Assert.Equal(1500, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task ProcessAsync_ApiKeyConfigured_SendsBearerToken()
    {
        _config.BaseUrl = "https://ollama.com";
        _config.ApiKey = "cloud-key";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://ollama.com/api/chat", request.Uri!.ToString());
        Assert.Equal("Bearer cloud-key", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task ProcessAsync_SuccessResponse_ReturnsContent()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("enhanced text", result);
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
    public async Task ProcessAsync_ResponseWithoutCounts_StillSucceedsWithoutRecording()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"message":{"content":"ok"},"done":true}""");
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task ProcessAsync_UnknownModel_ConfigurationErrorWithPullHint()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.NotFound, """{"error":"model \"llama3.2\" not found, try pulling it first"}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("ollama pull", ex.Message);
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
    public async Task ProcessAsync_ServerError_SurfacesOllamaErrorMessage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.InternalServerError, """{"error":"model runner has unexpectedly stopped"}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.Contains("model runner has unexpectedly stopped", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_NetworkFailure_ConfigurationErrorWithBaseUrlHint()
    {
        var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Connection refused."));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains(_config.BaseUrl, ex.Message);
        Assert.Contains("Is Ollama running?", ex.Message);
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
    public async Task ProcessAsync_InvalidBaseUrl_ConfigurationError()
    {
        _config.BaseUrl = "not a url";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
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
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"done":true}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));
        Assert.Empty(_usageSink.Records);
    }
}
