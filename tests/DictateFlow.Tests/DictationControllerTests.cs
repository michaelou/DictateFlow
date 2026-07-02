using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="DictationController"/>.</summary>
public sealed class DictationControllerTests
{
    private readonly Mock<IAudioRecorder> _recorder = new();
    private readonly Mock<IHotkeyService> _hotkeys = new();
    private readonly Mock<IRecordingOverlay> _overlay = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly TestTimeProvider _time = new();
    private readonly AppSettings _appSettings = new();

    public DictationControllerTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);
        _recorder.Setup(r => r.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _recorder.Setup(r => r.StopAsync()).ReturnsAsync(() => new MemoryStream(new byte[44]));
    }

    private DictationController CreateController()
        => new(_recorder.Object, _hotkeys.Object, _overlay.Object, _settings.Object, _time,
            Mock.Of<ILogger<DictationController>>());

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
    public async Task LevelChanged_ForwardsLevelToOverlay()
    {
        var controller = CreateController();
        await controller.StartRecordingAsync();

        _recorder.Raise(r => r.LevelChanged += null, _recorder.Object, 0.42f);

        _overlay.Verify(o => o.UpdateLevel(0.42f), Times.Once);
    }
}
