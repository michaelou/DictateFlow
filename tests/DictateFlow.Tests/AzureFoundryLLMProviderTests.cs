using System.Net;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="AzureFoundryLLMProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class AzureFoundryLLMProviderTests
{
    private const string SuccessBody = """
        {
          "choices": [{ "message": { "role": "assistant", "content": "enhanced text" } }],
          "usage": { "prompt_tokens": 42, "completion_tokens": 17, "total_tokens": 59 }
        }
        """;

    private readonly RecordingUsageSink _usageSink = new();

    private readonly AppSettings _appSettings = new()
    {
        Llm =
        {
            Endpoint = "https://myresource.services.ai.azure.com",
            ApiKey = "test-key",
            DeploymentName = "gpt-4o",
            Temperature = 0.5,
            MaxTokens = 2000,
            TimeoutSeconds = 60,
        },
    };

    private static PromptContext Context(
        string systemPrompt = "system prompt", string transcript = "user transcript")
        => new(systemPrompt, transcript, Temperature: 0.2, MaxTokens: 1500, ModeName: "Email");

    private ISettingsService CreateSettings()
    {
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(s => s.Current).Returns(_appSettings);
        return mock.Object;
    }

    /// <summary>Creates a provider talking directly to the fake handler (no resilience pipeline).</summary>
    private AzureFoundryLLMProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateSettings(), _usageSink, TimeProvider.System,
            NullLogger<AzureFoundryLLMProvider>.Instance);

    /// <summary>
    /// Creates a provider through the real DI registration, so requests flow through the
    /// standard resilience pipeline (with near-zero retry delays for test speed).
    /// </summary>
    private AzureFoundryLLMProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateSettings());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddAzureFoundryLlm(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<AzureFoundryLLMProvider>();
    }

    [Fact]
    public async Task ProcessAsync_BuildsDeploymentUrlHeaderAndJsonBody()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://myresource.services.ai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-06-01",
            request.Uri!.ToString());
        Assert.Equal("test-key", request.ApiKeyHeader);
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
        Assert.Equal(UsageCategories.LlmEnhancement, record.Category);
        Assert.Equal(42, record.PromptTokens);
        Assert.Equal(17, record.CompletionTokens);
        Assert.Null(record.DurationSeconds);
    }

    [Fact]
    public async Task ProcessAsync_ResponseWithoutUsage_StillSucceedsWithoutRecording()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        var provider = CreateProvider(handler);

        var result = await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task ProcessAsync_EndpointWithFullPath_UsedAsIsWithApiVersionAppended()
    {
        _appSettings.Llm.Endpoint = "https://host/openai/deployments/custom/chat/completions";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.ProcessAsync(Context(), CancellationToken.None);

        Assert.Equal(
            "https://host/openai/deployments/custom/chat/completions?api-version=2024-06-01",
            handler.Requests[0].Uri!.ToString());
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
    public async Task ProcessAsync_TooManyRequests_RetriedThenSurfaced()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests);
        var provider = CreateProviderWithResilience(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.False(ex.IsConfigurationError);
        Assert.Contains("429", ex.Message);
        Assert.Equal(4, handler.Requests.Count); // initial attempt + 3 retries
    }

    [Fact]
    public async Task ProcessAsync_Timeout_HonorsConfiguredTimeoutSeconds()
    {
        _appSettings.Llm.TimeoutSeconds = 1;
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
        Assert.Contains(_appSettings.Llm.Endpoint, ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_InvalidEndpoint_ConfigurationError()
    {
        _appSettings.Llm.Endpoint = "not a url";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
    }

    [Fact]
    public async Task ProcessAsync_MissingApiKey_ConfigurationError()
    {
        _appSettings.Llm.ApiKey = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_MissingDeploymentName_ConfigurationError()
    {
        _appSettings.Llm.DeploymentName = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("deployment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_ResponseWithoutChoices_ProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"choices":[]}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.ProcessAsync(Context(), CancellationToken.None));
        Assert.Empty(_usageSink.Records);
    }
}
