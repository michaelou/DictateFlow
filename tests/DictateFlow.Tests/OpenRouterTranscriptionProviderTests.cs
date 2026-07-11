using System.Net;
using System.Text.Json;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.OpenRouter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="OpenRouterTranscriptionProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class OpenRouterTranscriptionProviderTests
{
    private const string SuccessBody = """
        {"choices":[{"message":{"role":"assistant","content":"hello world"}}]}
        """;

    private readonly RecordingUsageSink _usageSink = new();

    private readonly OpenRouterTranscriptionConfig _config = new()
    {
        Endpoint = "https://openrouter.ai/api/v1",
        ApiKey = "sk-or-test",
        Model = "google/gemini-2.5-flash",
        Language = "",
        TimeoutSeconds = 60,
    };

    private IProviderConfigReader CreateConfigReader()
        => new TestProviderConfigReader().Set(ProviderKind.Transcription, OpenRouterProviders.RegistrationName, _config);

    private OpenRouterTranscriptionProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateConfigReader(), _usageSink, TimeProvider.System,
            NullLogger<OpenRouterTranscriptionProvider>.Instance);

    private OpenRouterTranscriptionProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateConfigReader());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddOpenRouterTranscription(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<OpenRouterTranscriptionProvider>();
    }

    private static MemoryStream OneSecondWav() => SilentWavFactory.Create(TimeSpan.FromSeconds(1));

    [Fact]
    public async Task TranscribeAsync_BuildsUrlBearerHeaderAndAudioContentPart()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.Uri!.ToString());
        Assert.Equal("Bearer sk-or-test", request.Headers["Authorization"]);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("google/gemini-2.5-flash", body.RootElement.GetProperty("model").GetString());
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Contains("Transcribe", content[0].GetProperty("text").GetString());
        Assert.Equal("input_audio", content[1].GetProperty("type").GetString());
        var inputAudio = content[1].GetProperty("input_audio");
        Assert.Equal("wav", inputAudio.GetProperty("format").GetString());
        Assert.False(string.IsNullOrEmpty(inputAudio.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task TranscribeAsync_LanguageHint_IncludedInInstruction()
    {
        _config.Language = "en-US";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        var instruction = body.RootElement.GetProperty("messages")[0].GetProperty("content")[0]
            .GetProperty("text").GetString();
        Assert.Contains("en-US", instruction);
        Assert.Equal("en-US", result.Language);
    }

    [Fact]
    public async Task TranscribeAsync_SuccessResponse_ParsedAndDurationReported()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("hello world", result.Text);
        // 1 s of 16 kHz/16-bit/mono audio = 32,000 data bytes → ~1.0 s.
        Assert.NotNull(result.AudioDurationSeconds);
        Assert.Equal(1.0, result.AudioDurationSeconds!.Value, precision: 2);

        var record = Assert.Single(_usageSink.Records);
        Assert.Equal(UsageCategories.Speech, record.Category);
        Assert.Equal(1.0, record.DurationSeconds!.Value, precision: 2);
        Assert.Null(record.PromptTokens);
    }

    [Fact]
    public async Task TranscribeAsync_ContentWithSurroundingWhitespace_Trimmed()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"choices":[{"message":{"content":"  hello world  "}}]}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("hello world", result.Text);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyTranscript_ReturnsEmptyTextWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"choices":[{"message":{"content":""}}]}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task TranscribeAsync_ErrorObjectInSuccessBody_SurfacedAsProviderException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"error":{"message":"Model does not support audio input."}}""");
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.Contains("audio input", ex.Message);
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task TranscribeAsync_NoChoices_ProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"choices":[]}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));
        Assert.Empty(_usageSink.Records);
    }

    [Fact]
    public async Task TranscribeAsync_Unauthorized_ConfigurationErrorWithoutRetry()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized);
        var provider = CreateProviderWithResilience(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TranscribeAsync_ServerError_RetriedThenSurfaced()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        var provider = CreateProviderWithResilience(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.False(ex.IsConfigurationError);
        Assert.Contains("500", ex.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task TranscribeAsync_MissingApiKey_ConfigurationError()
    {
        _config.ApiKey = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_MissingModel_ConfigurationError()
    {
        _config.Model = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscribeAsync_Timeout_HonorsConfiguredTimeoutSeconds()
    {
        _config.TimeoutSeconds = 1;
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
    }
}
