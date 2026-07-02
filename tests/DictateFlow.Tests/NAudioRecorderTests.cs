using DictateFlow.App.Services.Audio;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for the <see cref="NAudioRecorder"/> state machine. Only device-free paths are
/// covered — starting a capture needs real audio hardware.
/// </summary>
public sealed class NAudioRecorderTests
{
    private static NAudioRecorder CreateRecorder(out Mock<ISettingsService> settings)
    {
        settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(new AppSettings());
        return new NAudioRecorder(settings.Object, Mock.Of<ILogger<NAudioRecorder>>());
    }

    [Fact]
    public void IsRecording_Initially_False()
    {
        using var recorder = CreateRecorder(out _);

        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_ThrowsInvalidOperation()
    {
        // Documented contract: StopAsync without a matching StartAsync throws.
        using var recorder = CreateRecorder(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(recorder.StopAsync);
    }
}
