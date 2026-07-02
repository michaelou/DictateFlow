using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="DictationController"/>: recording lifecycle, hotkey handling,
/// silence auto-stop, and the hand-off of completed captures to <see cref="IDictationPipeline"/>.
/// The pipeline's internal behavior is covered by <see cref="DictationPipelineTests"/>.
/// </summary>
public sealed class DictationControllerTests
{
    private readonly Mock<IAudioRecorder> _recorder = new();
    private readonly Mock<IHotkeyService> _hotkeys = new();
    private readonly Mock<IRecordingOverlay> _overlay = new();
    private readonly Mock<IDictationPipeline> _pipeline = new();
    private readonly Mock<IForegroundAppService> _foregroundApp = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly TestTimeProvider _time = new();
    private readonly AppSettings _appSettings = new();

    public DictationControllerTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _recorder.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // Header-only capture: contains no audio, so the pipeline is skipped by default.
        _recorder.Setup(r => r.StopAsync()).ReturnsAsync(() => new MemoryStream(new byte[44]));
        _foregroundApp.SetupGet(f => f.LastCaptured).Returns("notepad");
        _foregroundApp.SetupGet(f => f.LastCapturedWindowHandle).Returns(1234);
        _pipeline.Setup(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineResult(true, "final text", "raw transcript", null));
    }

    private DictationController CreateController()
        => new(_recorder.Object, _hotkeys.Object, _overlay.Object, _pipeline.Object,
            _foregroundApp.Object, _settings.Object, _time, Mock.Of<ILogger<DictationController>>());

    /// <summary>Makes the recorder return a capture that contains audio beyond the WAV header.</summary>
    private void SetupCaptureWithAudio(int audioBytes = 32000)
        => _recorder.Setup(r => r.StopAsync()).ReturnsAsync(() => new MemoryStream(new byte[44 + audioBytes]));

    [Fact]
    public async Task StartRecordingAsync_StartsRecorderAndShowsOverlay()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();

        Assert.True(controller.IsRecording);
        _recorder.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _overlay.Verify(o => o.ShowListening(), Times.Once);
    }

    [Fact]
    public async Task StartRecordingAsync_CapturesForegroundApplication()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();

        _foregroundApp.Verify(f => f.Capture(), Times.Once);
    }

    [Fact]
    public async Task StartRecordingAsync_Twice_SecondCallIgnored()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StartRecordingAsync();

        _recorder.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_WithoutStart_IsIgnored()
    {
        var controller = CreateController();

        await controller.StopRecordingAsync();

        _recorder.Verify(r => r.StopAsync(), Times.Never);
        Assert.Null(controller.LastCapture);
    }

    [Fact]
    public async Task StartThenStop_StopsRecorderHidesOverlayAndKeepsCapture()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.False(controller.IsRecording);
        Assert.NotNull(controller.LastCapture);
        _recorder.Verify(r => r.StopAsync(), Times.Once);
        _overlay.Verify(o => o.Hide(), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_ReplacesAndDisposesPreviousCapture()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();
        var first = controller.LastCapture!;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.NotSame(first, controller.LastCapture);
        Assert.False(first.CanRead); // disposed
    }

    [Fact]
    public async Task StartRecordingAsync_WhenRecorderThrows_ResetsStateAndHidesOverlay()
    {
        _recorder.Setup(r => r.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no device"));
        var controller = CreateController();

        await controller.StartRecordingAsync();

        Assert.False(controller.IsRecording);
        _overlay.Verify(o => o.Hide(), Times.Once);
    }

    [Fact]
    public void HotkeyPressed_ToggleMode_StartsThenStops()
    {
        _appSettings.Recording.Mode = RecordingModes.Toggle;
        var controller = CreateController();

        _hotkeys.Raise(h => h.HotkeyPressed += null, EventArgs.Empty);
        Assert.True(controller.IsRecording);

        _hotkeys.Raise(h => h.HotkeyPressed += null, EventArgs.Empty);
        Assert.False(controller.IsRecording);

        _recorder.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _recorder.Verify(r => r.StopAsync(), Times.Once);
    }

    [Fact]
    public void HotkeyPressedAndReleased_PushToTalkMode_StartsAndStops()
    {
        _appSettings.Recording.Mode = RecordingModes.PushToTalk;
        var controller = CreateController();

        _hotkeys.Raise(h => h.HotkeyPressed += null, EventArgs.Empty);
        Assert.True(controller.IsRecording);

        _hotkeys.Raise(h => h.HotkeyReleased += null, EventArgs.Empty);
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void HotkeyReleased_ToggleMode_IsIgnored()
    {
        _appSettings.Recording.Mode = RecordingModes.Toggle;
        var controller = CreateController();

        _hotkeys.Raise(h => h.HotkeyPressed += null, EventArgs.Empty);
        _hotkeys.Raise(h => h.HotkeyReleased += null, EventArgs.Empty);

        Assert.True(controller.IsRecording);
        _recorder.Verify(r => r.StopAsync(), Times.Never);
    }

    [Fact]
    public void SettingsChanged_ReappliesHotkey()
    {
        _ = CreateController();

        _settings.Raise(s => s.SettingsChanged += null, this, _appSettings);

        _hotkeys.Verify(h => h.Apply(_appSettings.Recording), Times.Once);
    }

    [Fact]
    public async Task SilenceTimeout_QuietLevels_AutoStops()
    {
        _appSettings.Recording.SilenceTimeoutSeconds = 5;
        var controller = CreateController();
        await controller.StartRecordingAsync();

        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.01f);
        _time.Advance(TimeSpan.FromSeconds(6));
        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.01f);

        Assert.False(controller.IsRecording);
        _recorder.Verify(r => r.StopAsync(), Times.Once);
    }

    [Fact]
    public async Task SilenceTimeout_LoudLevelResetsTimer()
    {
        _appSettings.Recording.SilenceTimeoutSeconds = 5;
        var controller = CreateController();
        await controller.StartRecordingAsync();

        _time.Advance(TimeSpan.FromSeconds(4));
        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.5f); // speech resets the timer
        _time.Advance(TimeSpan.FromSeconds(4));
        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.01f);

        Assert.True(controller.IsRecording); // only 4 s of silence since the loud level

        _time.Advance(TimeSpan.FromSeconds(2));
        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.01f);

        Assert.False(controller.IsRecording); // 6 s of silence — timed out
    }

    [Fact]
    public async Task SilenceTimeout_ZeroSeconds_NeverAutoStops()
    {
        _appSettings.Recording.SilenceTimeoutSeconds = 0;
        var controller = CreateController();
        await controller.StartRecordingAsync();

        _time.Advance(TimeSpan.FromHours(1));
        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.0f);

        Assert.True(controller.IsRecording);
    }

    [Fact]
    public async Task StopRecordingAsync_HeaderOnlyCapture_SkipsPipeline()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        _pipeline.Verify(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _overlay.Verify(o => o.ShowProcessing(), Times.Never);
        _overlay.Verify(o => o.Hide(), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_WithAudio_RunsPipelineWithCapturedContext()
    {
        SetupCaptureWithAudio();
        PipelineRequest? request = null;
        _pipeline.Setup(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineRequest, CancellationToken>((r, _) => request = r)
            .ReturnsAsync(new PipelineResult(true, "final text", "raw transcript", null));
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.NotNull(request);
        Assert.Same(controller.LastCapture, request!.Audio);
        Assert.Equal("notepad", request.ApplicationName);
        Assert.Equal(1234, request.TargetWindowHandle);
        _overlay.Verify(o => o.ShowProcessing(), Times.Once);
        _overlay.Verify(o => o.ShowSuccess(), Times.Once);
        _overlay.Verify(o => o.ShowError(), Times.Never);
    }

    [Fact]
    public async Task StopRecordingAsync_PipelineFails_ShowsErrorAndRaisesFailed()
    {
        SetupCaptureWithAudio();
        _pipeline.Setup(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineResult(false, null, null, "bad key — check settings"));
        var controller = CreateController();
        string? failure = null;
        controller.DictationFailed += (_, message) => failure = message;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync(); // must not throw — the app keeps running

        Assert.Equal("bad key — check settings", failure);
        Assert.False(controller.IsRecording);
        _overlay.Verify(o => o.ShowError(), Times.Once);
        _overlay.Verify(o => o.ShowSuccess(), Times.Never);
    }

    [Fact]
    public async Task StopRecordingAsync_PipelineCancelled_HidesOverlayWithoutError()
    {
        SetupCaptureWithAudio();
        // Success with no final text = the user cancelled in the preview dialog.
        _pipeline.Setup(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipelineResult(true, null, "raw transcript", null));
        var controller = CreateController();
        string? failure = null;
        controller.DictationFailed += (_, message) => failure = message;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.Null(failure);
        _overlay.Verify(o => o.Hide(), Times.Once);
        _overlay.Verify(o => o.ShowSuccess(), Times.Never);
        _overlay.Verify(o => o.ShowError(), Times.Never);
    }

    [Fact]
    public async Task StopRecordingAsync_PipelineThrows_TreatedAsFailure()
    {
        SetupCaptureWithAudio();
        _pipeline.Setup(p => p.RunAsync(It.IsAny<PipelineRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var controller = CreateController();
        string? failure = null;
        controller.DictationFailed += (_, message) => failure = message;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync(); // must not throw — the app keeps running

        Assert.NotNull(failure);
        Assert.Contains("boom", failure);
        _overlay.Verify(o => o.ShowError(), Times.Once);
    }

    [Fact]
    public async Task LevelChanged_ForwardsLevelToOverlay()
    {
        var controller = CreateController();
        await controller.StartRecordingAsync();

        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.42f);

        _overlay.Verify(o => o.UpdateLevel(0.42f), Times.Once);
    }
}
