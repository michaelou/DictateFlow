using System.Net;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="AzureFoundryTranscriptionProvider"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved.
/// </summary>
public sealed class AzureFoundryTranscriptionProviderTests
{
    private readonly AppSettings _appSettings = new()
    {
        Speech =
        {
            Endpoint = "https://myresource.services.ai.azure.com",
            ApiKey = "test-key",
            DeploymentName = "mai-transcribe",
            Language = "en-US",
            TimeoutSeconds = 30,
        },
    };

    private ISettingsService CreateSettings()
    {
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(s => s.Current).Returns(_appSettings);
        return mock.Object;
    }

    /// <summary>Creates a provider talking directly to the fake handler (no resilience pipeline).</summary>
    private AzureFoundryTranscriptionProvider CreateProvider(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), CreateSettings(), NullLogger<AzureFoundryTranscriptionProvider>.Instance);

    /// <summary>
    /// Creates a provider through the real DI registration, so requests flow through the
    /// standard resilience pipeline (with near-zero retry delays for test speed).
    /// </summary>
    private AzureFoundryTranscriptionProvider CreateProviderWithResilience(FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateSettings());
        services.AddAzureFoundryTranscription(options => options.Retry.Delay = TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<AzureFoundryTranscriptionProvider>();
    }

    private static MemoryStream OneSecondWav() => SilentWavFactory.Create(TimeSpan.FromSeconds(1));

    [Fact]
    public async Task TranscribeAsync_BuildsDeploymentUrlHeaderAndMultipartFields()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"text":"hello"}""");
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://myresource.services.ai.azure.com/openai/deployments/mai-transcribe/audio/transcriptions?api-version=2024-06-01",
            request.Uri!.ToString());
        Assert.Equal("test-key", request.ApiKeyHeader);
        Assert.StartsWith("multipart/form-data", request.ContentType);
        Assert.Contains("name=\"file\"", request.Body);
        Assert.Contains("filename=\"audio.wav\"", request.Body);
        Assert.Contains("Content-Type: audio/wav", request.Body);
        Assert.Contains("name=\"response_format\"", request.Body);
        Assert.Contains("json", request.Body);
        Assert.Contains("name=\"language\"", request.Body);
        Assert.Contains("en-US", request.Body);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyLanguage_OmitsLanguageField()
    {
        _appSettings.Speech.Language = "";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"text":"hello"}""");
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.DoesNotContain("name=\"language\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task TranscribeAsync_EndpointWithFullPath_UsedAsIsWithApiVersionAppended()
    {
        _appSettings.Speech.Endpoint = "https://myresource.services.ai.azure.com/openai/deployments/custom/audio/transcriptions";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"text":"hello"}""");
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal(
            "https://myresource.services.ai.azure.com/openai/deployments/custom/audio/transcriptions?api-version=2024-06-01",
            handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task TranscribeAsync_EndpointWithExistingApiVersion_NotDuplicated()
    {
        _appSettings.Speech.Endpoint = "https://host/openai/deployments/d/audio/transcriptions?api-version=preview";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"text":"hello"}""");
        var provider = CreateProvider(handler);

        await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal(
            "https://host/openai/deployments/d/audio/transcriptions?api-version=preview",
            handler.Requests[0].Uri!.ToString());
    }

    [Fact]
    public async Task TranscribeAsync_SuccessResponse_ParsedIntoResult()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"text":"hello world","duration":2.5,"language":"en","extra_field":123}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        Assert.Equal("hello world", result.Text);
        Assert.Equal(2.5, result.AudioDurationSeconds);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task TranscribeAsync_NoDurationInResponse_ComputedFromWavLength()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"text":"hello"}""");
        var provider = CreateProvider(handler);

        var result = await provider.TranscribeAsync(OneSecondWav(), CancellationToken.None);

        // 1 s of 16 kHz/16-bit/mono audio = 32,000 data bytes.
        Assert.NotNull(result.AudioDurationSeconds);
        Assert.Equal(1.0, result.AudioDurationSeconds!.Value, precision: 2);
        Assert.Equal("en-US", result.Language); // falls back to the configured language
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
        _appSettings.Speech.TimeoutSeconds = 1;
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
        Assert.Contains(_appSettings.Speech.Endpoint, ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_InvalidEndpoint_ConfigurationError()
    {
        _appSettings.Speech.Endpoint = "not a url";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
    }

    [Fact]
    public async Task TranscribeAsync_MissingApiKey_ConfigurationError()
    {
        _appSettings.Speech.ApiKey = "";
        var provider = CreateProvider(new FakeHttpMessageHandler(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));

        Assert.True(ex.IsConfigurationError);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_ResponseWithoutText_ProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status":"ok"}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.TranscribeAsync(OneSecondWav(), CancellationToken.None));
    }
}
