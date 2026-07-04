using System.Net;
using DictateFlow.Core.Services.Diagnostics;
using DictateFlow.Core.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="GitHubUpdateService"/>: it reports an available update, reports
/// up-to-date, and turns every network/GitHub failure into a graceful failed result rather
/// than throwing.
/// </summary>
public sealed class GitHubUpdateServiceTests
{
    private static GitHubUpdateService CreateService(HttpMessageHandler handler, string currentVersion)
    {
        var diagnostics = new Mock<IDiagnosticsService>();
        diagnostics.SetupGet(d => d.AppVersion).Returns(currentVersion);
        var httpClient = new HttpClient(handler);
        return new GitHubUpdateService(httpClient, diagnostics.Object, NullLogger<GitHubUpdateService>.Instance);
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/michaelou/DictateFlow/releases/tag/v0.2.0",
          "body": "- Faster startup\n- Bug fixes",
          "prerelease": false,
          "assets": [
            {
              "name": "DictateFlowPortable-v0.2.0.zip",
              "browser_download_url": "https://github.com/michaelou/DictateFlow/releases/download/v0.2.0/DictateFlowPortable-v0.2.0.zip",
              "size": 123
            },
            {
              "name": "DictateFlowSetup-v0.2.0.exe",
              "browser_download_url": "https://github.com/michaelou/DictateFlow/releases/download/v0.2.0/DictateFlowSetup-v0.2.0.exe",
              "size": 456789
            }
          ]
        }
        """;

    private const string ReleaseJsonNoInstaller = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/michaelou/DictateFlow/releases/tag/v0.2.0",
          "body": "- Faster startup",
          "prerelease": false,
          "assets": [
            {
              "name": "DictateFlowPortable-v0.2.0.zip",
              "browser_download_url": "https://github.com/michaelou/DictateFlow/releases/download/v0.2.0/DictateFlowPortable-v0.2.0.zip",
              "size": 123
            }
          ]
        }
        """;

    [Fact]
    public async Task CheckForUpdates_NewerReleaseAvailable_ReportsUpdate()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleaseJson);
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.0", result.CurrentVersion);
        Assert.Equal("0.2.0", result.LatestVersion);
        Assert.Equal("https://github.com/michaelou/DictateFlow/releases/tag/v0.2.0", result.ReleaseUrl);
        Assert.Contains("Faster startup", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdates_NewerReleaseWithInstallerAsset_ExposesInstaller()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleaseJson);
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.True(result.HasInstaller);
        Assert.Equal(
            "https://github.com/michaelou/DictateFlow/releases/download/v0.2.0/DictateFlowSetup-v0.2.0.exe",
            result.InstallerUrl);
        Assert.Equal(456789, result.InstallerSize);
    }

    [Fact]
    public async Task CheckForUpdates_NewerReleaseWithoutInstallerAsset_ReportsUpdateButNoInstaller()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleaseJsonNoInstaller);
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.HasInstaller);
        Assert.Null(result.InstallerUrl);
        Assert.Equal(0, result.InstallerSize);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReportsUpToDate()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleaseJson);
        var service = CreateService(handler, "0.2.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.2.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SendsGitHubApiHeaders()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ReleaseJson);
        var service = CreateService(handler, "0.2.0");

        await service.CheckForUpdatesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/michaelou/DictateFlow/releases/latest", request.Uri?.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.GetValueOrDefault("Accept", ""));
    }

    [Fact]
    public async Task CheckForUpdates_HttpError_ReportsFailureGracefully()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("0.1.0", result.CurrentVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CheckForUpdates_NetworkError_ReportsFailureGracefully()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("offline"));
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CheckForUpdates_MalformedJson_ReportsFailureGracefully()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{ not json");
        var service = CreateService(handler, "0.1.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }
}
