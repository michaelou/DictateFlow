using System.Text.Json;
using DictateFlow.App.Services;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Diagnostics;
using DictateFlow.Core.Services.Transfer;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="DiagnosticsService"/>: the copy-diagnostics report never contains
/// API keys, and the log tail returns the last lines of the newest log file.
/// </summary>
public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly TestAppPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    private DiagnosticsService CreateService(AppSettings settings)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(s => s.Current).Returns(settings);
        var transfer = new SettingsTransfer(
            [new LegacySettingsMigration(NullLogger<LegacySettingsMigration>.Instance)],
            NullLogger<SettingsTransfer>.Instance);
        return new DiagnosticsService(
            _paths, settingsService.Object, transfer, NullLogger<DiagnosticsService>.Instance);
    }

    [Fact]
    public void BuildReport_ContainsPathsAndVersions_ButNoApiKeys()
    {
        var settings = new AppSettings();
        settings.Providers.Llm["AzureFoundry"] = JsonSerializer.SerializeToElement(new
        {
            Endpoint = "https://llm.example.com",
            ApiKey = "super-secret-key",
            DeploymentName = "gpt",
        });

        var report = CreateService(settings).BuildReport();

        Assert.Contains(_paths.SettingsFilePath, report);
        Assert.Contains(_paths.LogsDirectory, report);
        Assert.Contains("https://llm.example.com", report);
        Assert.DoesNotContain("super-secret-key", report);
    }

    [Fact]
    public void ReadLogTail_ReturnsTheLastLinesOfTheNewestLog()
    {
        var older = Path.Combine(_paths.LogsDirectory, "dictateflow-20260701.log");
        File.WriteAllLines(older, ["old-1", "old-2"]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        var newest = Path.Combine(_paths.LogsDirectory, "dictateflow-20260703.log");
        File.WriteAllLines(newest, Enumerable.Range(1, 150).Select(i => $"line-{i}"));

        var tail = CreateService(new AppSettings()).ReadLogTail(100);

        Assert.Equal(100, tail.Count);
        Assert.Equal("line-51", tail[0]);
        Assert.Equal("line-150", tail[^1]);
    }

    [Fact]
    public void ReadLogTail_WithNoLogFiles_ExplainsInsteadOfThrowing()
    {
        var tail = CreateService(new AppSettings()).ReadLogTail(100);

        Assert.Equal(["No log file found."], tail);
    }
}
