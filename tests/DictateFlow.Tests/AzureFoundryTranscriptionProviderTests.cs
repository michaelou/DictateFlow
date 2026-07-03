using System.Net;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="AzureFoundryTranscriptionProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class AzureFoundryTranscriptionProviderTests
{
    private readonly RecordingUsageSink _usageSink = new();

    private readonly AzureFoundryTranscriptionConfig _config = new()
    {
        Endpoint = "https://myresource.cognitiveservices.azure.com",
        ApiKey = "test-key",
        DeploymentName = "mai-transcribe",
        Language = "en-US",
        TimeoutSeconds = 30,
    };

    private IProviderConfigReader CreateConfigReader()
        => new TestProviderConfigReader()
            .Set(ProviderKind.Transcription, AzureFoundryProviders.RegistrationName, _config);

    /// <summary>Creates a provider talking directly to the fake handler (no resilience pipeline).</summary>
    private AzureFoundryTranscriptionProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateConfigReader(), _usageSink, TimeProvider.System,
            NullLogger<AzureFoundryTranscriptionProvider>.Instance);

    /// <summary>
    /// Creates a provider through the real DI registration, so requests flow through the
    /// standard resilience pipeline (with near-zero retry delays for test speed).
    /// </summary>
    private AzureFoundryTranscriptionProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateConfigReader());
        services.AddSingleton<IUsageSink>(_usageSink);
        services.AddSingleton(TimeProvider.System);
        services.AddAzureFoundryTranscription(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<AzureFoundryTranscriptionProvider>();
    }

    private static MemoryStream OneSecondWav() => SilentWavFactory.Create(TimeSpan.FromSeconds(1));

    private const string SuccessBody =
        """{"durationMilliseconds":1000,"combinedPhrases":[{"text":"hello"}]}""";

    [Fact]
    public async Task TranscribeAsync_BuildsTranscribeUrlHeaderAndMultipartFields()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://myresource.cognitiveservices.azure.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15",
            request.Uri!.ToString());
        Assert.Equal("test-key", request.Headers["Ocp-Apim-Subscription-Key"]);
        Assert.StartsWith("multipart/form-data", request.ContentType);
        Assert.Contains("name=\"audio\"", request.Body);
        Assert.Contains("filename=\"audio.wav\"", request.Body);
        Assert.Contains("Content-Type: audio/wav", request.Body);
        Assert.Contains("name=\"definition\"", request.Body);
        Assert.Contains("\"locales\"", request.Body);
        Assert.Contains("en-US", request.Body);
        Assert.Contains("\"enhancedMode\"", request.Body); // DeploymentName is set
        Assert.Contains("mai-transcribe", request.Body);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyLanguage_OmitsLocales()
    {
        _config.Language = "";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.DoesNotContain("\"locales\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyDeployment_OmitsEnhancedMode()
    {
        _config.DeploymentName = "";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.DoesNotContain("\"enhancedMode\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task TranscribeAsync_EndpointWithFullPath_UsedAsIsWithApiVersionAppended()
    {
        _config.Endpoint = "https://myresource.cognitiveservices.azure.com/speechtotext/transcriptions:transcribe";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal(
            "https://myresource.cognitiveservices.azure.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15",
            handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task TranscribeAsync_EndpointWithExistingApiVersion_NotDuplicated()
    {
        _config.Endpoint = "https://host/speechtotext/transcriptions:transcribe?api-version=preview";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal(
            "https://host/speechtotext/transcriptions:transcribe?api-version=preview",
            handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task TranscribeAsync_SuccessResponse_ParsedIntoResult()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"durationMilliseconds":2500,"combinedPhrases":[{"text":"hello world"}],"phrases":[{"locale":"en"}],"extra_field":123}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("hello world", result.Text);
        Assert.Equal(2.5, result.AudioDurationSeconds);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task TranscribeAsync_NoDurationInResponse_ComputedFromWavLength()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"combinedPhrases":[{"text":"hello"}]}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        // 1 s of 16 kHz/16-bit/mono audio = 32,000 data bytes.
        Assert.NotNull(result.AudioDurationSeconds);
        Assert.Equal(1.0, result.AudioDurationSeconds!.Value, precision: 2);
        Assert.Equal("en-US", result.Language); // falls back to the configured language
    }

    [Fact]
    public async Task TranscribeAsync_Success_ReportsAudioDurationUsage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"durationMilliseconds":2500,"combinedPhrases":[{"text":"hello"}]}""");
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        var record = Assert.Single(_usageSink.Records);
        Assert.Equal(UsageCategories.Speech, record.Category);
        Assert.Equal(2.5, record.DurationSeconds);
        Assert.Null(record.PromptTokens);
        Assert.Null(record.CompletionTokens);
    }

    [Fact]
    public async Task TranscribeAsync_Failure_ReportsNoUsage()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
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
        Assert.Single(handler.Requests); // 401 must not be retried
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
        Assert.Equal(4, handler.Requests.Count); // initial attempt + 3 retries
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

    [Fact]
    public async Task TranscribeAsync_CallerCancellation_NotWrappedAsProviderException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.TranscribeAsync(OneSecondWav(), cts.Token));
    }

    [Fact]
    public async Task TranscribeAsync_NetworkFailure_ConfigurationErrorWithEndpointHint()
    {
        var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("No such host is known."));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains(_config.Endpoint, ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_InvalidEndpoint_ConfigurationError()
    {
        _config.Endpoint = "not a url";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
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
    public async Task TranscribeAsync_UnrecognizedResponse_ProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status":"ok"}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));
    }

    [Fact]
    public async Task TranscribeAsync_EmptyTranscript_ReturnsEmptyTextWithoutThrowing()
    {
        // Silence yields a well-formed 200 with no phrases; the Test connection check relies on
        // this not being treated as a failure.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"durationMilliseconds":500,"combinedPhrases":[]}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("", result.Text);
    }
}
