using System.Net;
using System.Security.Cryptography;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Providers.Parakeet;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="ParakeetModelManager"/> against a fake
/// <see cref="HttpMessageHandler"/> — no network involved. Definitions are test-local with
/// checksums computed from the fake payloads, so verification runs for real.
/// </summary>
public sealed class ParakeetModelManagerTests : IDisposable
{
    private readonly TestAppPaths _appPaths = new();

    public void Dispose() => _appPaths.Dispose();

    private ParakeetModelManager CreateManager(byte[] payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(payload),
        }));
        return new ParakeetModelManager(
            new FakeHttpClientFactory(handler), _appPaths, NullLogger<ParakeetModelManager>.Instance);
    }

    private static ModelDefinition ModelDefinitionFor(byte[] payload, string? sha256 = null) => new(
        ParakeetModelCatalog.EngineName,
        "test.onnx",
        "Test Component",
        ModelComponentKind.Model,
        new Uri("https://example.test/test.onnx"),
        payload.Length,
        sha256 ?? Convert.ToHexStringLower(SHA256.HashData(payload)));

    [Fact]
    public void Catalog_HasAllFourComponents_NoEngine()
    {
        Assert.Equal(4, ParakeetModelCatalog.All.Count);
        Assert.All(ParakeetModelCatalog.All, d => Assert.Equal(ModelComponentKind.Model, d.Kind));
        Assert.All(ParakeetModelCatalog.All, d => Assert.Equal(64, d.Sha256.Length));
    }

    [Fact]
    public async Task DownloadAsync_InstallsComponent_VerifiedAndImmediatelyAvailable()
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
    public async Task DownloadAsync_HttpFailure_ActionableProviderException()
    {
        var definition = ModelDefinitionFor([1, 2, 3]);
        var manager = CreateManager([], HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None));

        Assert.Contains("503", ex.Message);
        Assert.False(manager.IsInstalled(definition));
    }

    [Fact]
    public async Task DeleteAsync_RemovesInstalledComponent()
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
    public async Task VerifyAsync_TamperedFile_ReturnsFalse()
    {
        var payload = new byte[512];
        Random.Shared.NextBytes(payload);
        var definition = ModelDefinitionFor(payload);
        var manager = CreateManager(payload);
        await manager.DownloadAsync(
            definition, new SynchronousProgress<double>(_ => { }), CancellationToken.None);

        // Same size, different content: IsInstalled (size check) stays true, the hash catches it.
        var tampered = (byte[])payload.Clone();
        tampered[0] ^= 0xFF;
        await File.WriteAllBytesAsync(manager.GetModelPath(definition), tampered);

        Assert.True(manager.IsInstalled(definition));
        Assert.False(await manager.VerifyAsync(definition, CancellationToken.None));
    }

    [Fact]
    public void IsFullyInstalled_FalseWhileAnyCatalogFileMissing()
    {
        var manager = CreateManager([]);

        Assert.False(manager.IsFullyInstalled());
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
