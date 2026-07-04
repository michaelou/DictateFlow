using System.Net;
using DictateFlow.Core.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="UpdateDownloader"/>: it writes the installer to disk, reports progress,
/// verifies the finished size, and rejects a truncated download.
/// </summary>
public sealed class UpdateDownloaderTests
{
    private const string Url = "https://github.com/michaelou/DictateFlow/releases/download/v0.2.0/DictateFlowSetup-v0.2.0.exe";

    private static FakeHttpMessageHandler HandlerReturning(byte[] payload) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));

    private static UpdateDownloader CreateDownloader(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<UpdateDownloader>.Instance);

    [Fact]
    public async Task DownloadAsync_WritesFileAndReportsProgress()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var downloader = CreateDownloader(HandlerReturning(payload));
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        var path = await downloader.DownloadAsync(Url, payload.Length, progress);

        try
        {
            Assert.True(File.Exists(path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.EndsWith(".exe", path);
            // Progress is fire-and-forget via the synchronization context; the final verified
            // report of 1.0 is raised synchronously after the copy, so it is always observed.
            Assert.Contains(1.0, reported);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DownloadAsync_SizeMismatch_ThrowsAndDeletesFile()
    {
        var payload = new byte[] { 1, 2, 3 };
        var downloader = CreateDownloader(HandlerReturning(payload));

        // Claim a larger expected size than the server actually returns.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(Url, payload.Length + 100, progress: null));

        Assert.Contains("expected", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The corrupt download must not be left behind for the app to launch.
        var expectedPath = Path.Combine(Path.GetTempPath(), "DictateFlow", "DictateFlowSetup-v0.2.0.exe");
        Assert.False(File.Exists(expectedPath));
    }

    [Fact]
    public async Task DownloadAsync_HttpError_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound);
        var downloader = CreateDownloader(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(Url, expectedSize: 10, progress: null));
    }
}
