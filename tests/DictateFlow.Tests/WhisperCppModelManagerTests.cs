using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Providers.WhisperCpp;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="WhisperCppModelManager"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved. Definitions are test-local with
/// checksums computed from the fake payloads, so verification runs for real.
/// </summary>
public sealed class WhisperCppModelManagerTests : IDisposable
{
    private readonly TestAppPaths _appPaths = new();

    public void Dispose() => _appPaths.Dispose();

    private WhisperCppModelManager CreateManager(byte[] payload)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));
        return new WhisperCppModelManager(
            new FakeHttpClientFactory(handler), _appPaths, NullLogger<WhisperCppModelManager>.Instance);
    }

    private static ModelDefinition ModelDefinitionFor(byte[] payload, string? sha256 = null) => new(
        WhisperCppModelCatalog.EngineName,
        "ggml-test",
        "Test Model",
        ModelComponentKind.Model,
        new Uri("https://example.test/ggml-test.bin"),
        payload.Length,
        sha256 ?? Convert.ToHexStringLower(SHA256.HashData(payload)));

    private static byte[] EngineZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("Release/" + WhisperCppModelCatalog.EngineExecutableName).Open();
            entry.Write("fake exe"u8);
        }

        return buffer.ToArray();
    }

    private static ModelDefinition EngineDefinitionFor(byte[] payload) => new(
        WhisperCppModelCatalog.EngineName,
        "engine",
        "Test Engine",
        ModelComponentKind.Engine,
        new Uri("https://example.test/whisper-bin-x64.zip"),
        payload.Length,
        Convert.ToHexStringLower(SHA256.HashData(payload)));

    [Fact]
    public async Task DownloadAsync_InstallsModel_VerifiedAndImmediatelyAvailable()
    {
        var payload = new byte[128 * 1024];
        Random.Shared.NextBytes(payload);
        var definition = ModelDefinitionFor(payload);
        var manager = CreateManager(payload);
        var fractions = new List<double>();

        Assert.False(manager.IsInstalled(definition));
        await manager.DownloadAsync(
            definition, new SynchronousProgress<double>(fractions.Add), CancellationToken.None);

        Assert.True(manager.IsInstalled(definition));
        Assert.True(File.Exists(manager.GetModelPath(definition)));
        Assert.Equal(payload, await File.ReadAllBytesAsync(manager.GetModelPath(definition)));
        Assert.True(await manager.VerifyAsync(definition, CancellationToken.None));
        Assert.Equal(1.0, fractions[^1]);
        // GetInstalledModelsAsync discovers catalog components only — the ad-hoc test
        // definition is invisible to it, and the pinned catalog files are not installed here.
        Assert.Empty(await manager.GetInstalledModelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_RejectsCorruptedDownload_AndLeavesNothingBehind()
    {
        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);
        var definition = ModelDefinitionFor(payload, sha256: new string('0', 64));
        var manager = CreateManager(payload);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None));

        Assert.Contains("corrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsInstalled(definition));
        Assert.False(File.Exists(manager.GetModelPath(definition)));
        Assert.Empty(Directory.EnumerateFiles(manager.ModelDirectory));
    }

    [Fact]
    public async Task DownloadAsync_RejectsWrongSize()
    {
        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);
        var definition = ModelDefinitionFor(payload) with { SizeBytes = payload.Length + 1 };
        var manager = CreateManager(payload);

        await Assert.ThrowsAsync<ProviderException>(() => manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None));
        Assert.False(manager.IsInstalled(definition));
    }

    [Fact]
    public async Task DownloadAsync_InstallsEngine_ExtractsExecutableAndRegistersVersion()
    {
        var payload = EngineZip();
        var definition = EngineDefinitionFor(payload);
        var manager = CreateManager(payload);

        await manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None);

        Assert.True(manager.IsInstalled(definition));
        Assert.NotNull(manager.GetEngineExecutablePath());
        Assert.EndsWith(WhisperCppModelCatalog.EngineExecutableName, manager.GetEngineExecutablePath());
        Assert.Equal(WhisperCppModelCatalog.EngineVersion, manager.GetInstalledEngineVersion());
        Assert.True(await manager.VerifyAsync(definition, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(manager.EngineDirectory, "engine.download"))); // archive cleaned up
    }

    [Fact]
    public async Task DownloadAsync_EngineArchiveWithoutExecutable_IsRolledBack()
    {
        byte[] payload;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("Release/readme.txt").Open();
                entry.Write("no exe here"u8);
            }

            payload = buffer.ToArray();
        }

        var definition = EngineDefinitionFor(payload);
        var manager = CreateManager(payload);

        await Assert.ThrowsAsync<ProviderException>(() => manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None));
        Assert.False(manager.IsInstalled(definition));
        Assert.Null(manager.GetEngineExecutablePath());
    }

    [Fact]
    public async Task DeleteAsync_RemovesInstalledModel()
    {
        var payload = new byte[512];
        Random.Shared.NextBytes(payload);
        var definition = ModelDefinitionFor(payload);
        var manager = CreateManager(payload);
        await manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None);

        await manager.DeleteAsync(definition);

        Assert.False(manager.IsInstalled(definition));
        Assert.False(File.Exists(manager.GetModelPath(definition)));
    }

    [Fact]
    public async Task DownloadAsync_HttpFailure_ThrowsProviderException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var manager = new WhisperCppModelManager(
            new FakeHttpClientFactory(handler), _appPaths, NullLogger<WhisperCppModelManager>.Instance);
        var definition = ModelDefinitionFor([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public void Catalog_DefinitionsArePinnedAndWellFormed()
    {
        Assert.Equal(3, WhisperCppModelCatalog.All.Count);
        Assert.Equal(WhisperCppModelCatalog.All.Count, WhisperCppModelCatalog.All.Select(d => d.Id).Distinct().Count());
        foreach (var definition in WhisperCppModelCatalog.All)
        {
            Assert.True(definition.SizeBytes > 0);
            Assert.Equal(64, definition.Sha256.Length);
            Assert.True(definition.Sha256.All(Uri.IsHexDigit));
            Assert.Equal(Uri.UriSchemeHttps, definition.DownloadUri.Scheme);
        }

        Assert.Same(WhisperCppModelCatalog.Small, WhisperCppModelCatalog.FindModel("GGML-SMALL"));
        Assert.Same(WhisperCppModelCatalog.Medium, WhisperCppModelCatalog.FindModel(WhisperCppModelCatalog.MediumModelId));
        Assert.Null(WhisperCppModelCatalog.FindModel("ggml-large"));
    }

    /// <summary>Minimal <see cref="IHttpClientFactory"/> handing out clients over a fixed handler.</summary>
    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// <see cref="IProgress{T}"/> that invokes synchronously — <see cref="Progress{T}"/>
    /// posts to a synchronization context, which xunit tests do not pump.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
