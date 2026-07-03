using DictateFlow.App.Services.Audio;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NAudio.Wave;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for the <see cref="NAudioRecorder"/> state machine. Device-free paths always run;
/// the leak smoke test needs real audio hardware and no-ops on machines without any.
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

    [Fact]
    public async Task StartStop_FiftyConsecutiveSessions_LeaksNothingAndKeepsWorking()
    {
        if (WaveInEvent.DeviceCount == 0)
        {
            return; // no capture hardware (CI); the device-free contract tests still ran
        }

        // M8 resource-hygiene smoke check: 50 dictation-sized capture sessions must neither
        // throw nor wedge the recorder — each session opens and disposes its own device
        // handle, writer and buffer.
        using var recorder = CreateRecorder(out _);
        for (var i = 0; i < 50; i++)
        {
            await recorder.StartAsync(CancellationToken.None);
            Assert.True(recorder.IsRecording);

            await using var audio = await recorder.StopAsync();
            Assert.False(recorder.IsRecording);
            Assert.True(audio.Length >= 44, $"session {i}: expected at least a WAV header");
        }
    }
}
