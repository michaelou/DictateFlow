using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="DictationController"/>.</summary>
public sealed class DictationControllerTests
{
    private readonly Mock<IAudioRecorder> _recorder = new();
    private readonly Mock<IHotkeyService> _hotkeys = new();
    private readonly Mock<IRecordingOverlay> _overlay = new();
    private readonly Mock<ITranscriptionProvider> _transcription = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly TestTimeProvider _time = new();
    private readonly AppSettings _appSettings = new();

    public DictationControllerTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _recorder.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // Header-only capture: contains no audio, so transcription is skipped by default.
        _recorder.Setup(r => r.StopAsync()).ReturnsAsync(() => new MemoryStream(new byte[44]));
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("transcribed text", 1.0, "en-US"));
    }

    private DictationController CreateController()
        => new(_recorder.Object, _hotkeys.Object, _overlay.Object, _transcription.Object, _settings.Object,
            _time, Mock.Of<ILogger<DictationController>>());

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
        Assert.Equal(0, controller.LastCapture!.Position);
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
    public async Task StopRecordingAsync_HeaderOnlyCapture_SkipsTranscription()
    {
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        _transcription.Verify(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _overlay.Verify(o => o.ShowProcessing(), Times.Never);
        _overlay.Verify(o => o.Hide(), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_WithAudio_TranscribesAndRaisesCompleted()
    {
        SetupCaptureWithAudio();
        var controller = CreateController();
        TranscriptionResult? completed = null;
        controller.TranscriptionCompleted += (_, result) => completed = result;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.NotNull(completed);
        Assert.Equal("transcribed text", completed!.Text);
        _transcription.Verify(t => t.TranscribeAsync(controller.LastCapture!, CancellationToken.None), Times.Once);
        _overlay.Verify(o => o.ShowProcessing(), Times.Once);
        _overlay.Verify(o => o.Hide(), Times.Once);
        _overlay.Verify(o => o.ShowError(), Times.Never);
    }

    [Fact]
    public async Task StopRecordingAsync_TranscriptionReadsCaptureFromStart()
    {
        SetupCaptureWithAudio();
        long observedPosition = -1;
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((audio, _) => observedPosition = audio.Position)
            .ReturnsAsync(new TranscriptionResult("x", null, null));
        var controller = CreateController();

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.Equal(0, observedPosition);
    }

    [Fact]
    public async Task StopRecordingAsync_ProviderException_ShowsErrorAndRaisesFailed()
    {
        SetupCaptureWithAudio();
        var failure = new ProviderException("Test", "bad key", isConfigurationError: true);
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var controller = CreateController();
        ProviderException? raised = null;
        controller.TranscriptionFailed += (_, ex) => raised = ex;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync(); // must not throw — the app keeps running

        Assert.Same(failure, raised);
        Assert.False(controller.IsRecording);
        _overlay.Verify(o => o.ShowError(), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_UnexpectedException_WrapsIntoProviderException()
    {
        SetupCaptureWithAudio();
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var controller = CreateController();
        ProviderException? raised = null;
        controller.TranscriptionFailed += (_, ex) => raised = ex;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.NotNull(raised);
        Assert.IsType<InvalidOperationException>(raised!.InnerException);
        _overlay.Verify(o => o.ShowError(), Times.Once);
    }

    [Fact]
    public async Task StopRecordingAsync_WithMockProvider_ReturnsCannedText()
    {
        SetupCaptureWithAudio();
        var mock = new MockTranscriptionProvider { CannedText = "canned", Delay = TimeSpan.Zero };
        var controller = new DictationController(
            _recorder.Object, _hotkeys.Object, _overlay.Object, mock, _settings.Object,
            _time, Mock.Of<ILogger<DictationController>>());
        TranscriptionResult? completed = null;
        controller.TranscriptionCompleted += (_, result) => completed = result;

        await controller.StartRecordingAsync();
        await controller.StopRecordingAsync();

        Assert.Equal("canned", completed?.Text);
        Assert.Equal(1.0, completed?.AudioDurationSeconds); // 32,000 audio bytes → 1 s
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
