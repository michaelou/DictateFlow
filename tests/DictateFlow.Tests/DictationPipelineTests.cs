using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Pipeline;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="DictationPipeline"/> with all-mock providers and a pass-through
/// gate: step ordering, history/output hand-off, the LLM fallback, gate cancellation and
/// failure shapes.
/// </summary>
public sealed class DictationPipelineTests
{
    private readonly Mock<ITranscriptionProvider> _transcription = new();
    private readonly Mock<IPromptResolver> _resolver = new();
    private readonly Mock<ILLMProvider> _llm = new();
    private readonly Mock<IHistoryRepository> _history = new();
    private readonly Mock<IOutputProvider> _output = new();
    private readonly Mock<IOutputGate> _gate = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();
    private readonly List<string> _callOrder = [];

    public DictationPipelineTests()
    {
        _settings.SetupGet(s => s.Current).Returns(_appSettings);

        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback(() => _callOrder.Add("transcribe"))
            .ReturnsAsync(new TranscriptionResult("raw transcript", 1.0, "en-US"));
        _resolver.Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((transcript, mode) => new PromptContext("system prompt", transcript, 0.2, 2000, mode));
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => _callOrder.Add("llm"))
            .ReturnsAsync((PromptContext context, CancellationToken _) => "[enhanced] " + context.Transcript);
        _history.Setup(h => h.AddAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _callOrder.Add("history"))
            .Returns(Task.CompletedTask);
        _output.SetupGet(o => o.Name).Returns(OutputProviderNames.ClipboardPaste);
        _output.Setup(o => o.OutputAsync(It.IsAny<string>()))
            .Callback(() => _callOrder.Add("output"))
            .Returns(Task.CompletedTask);
        // Pass-through gate: confirms the draft unchanged, like Automatic mode.
        _gate.Setup(g => g.ConfirmAsync(It.IsAny<PipelineResult>()))
            .Callback(() => _callOrder.Add("gate"))
            .ReturnsAsync((PipelineResult draft) => draft.FinalText);
    }

    private DictationPipeline CreatePipeline(params IOutputProvider[] outputProviders)
        => new(_transcription.Object, _resolver.Object, _llm.Object, _history.Object,
            outputProviders.Length > 0 ? outputProviders : [_output.Object],
            _gate.Object, _settings.Object, TimeProvider.System,
            Mock.Of<ILogger<DictationPipeline>>());

    private static PipelineRequest CreateRequest()
        => new(new MemoryStream(new byte[44 + 32000]), "notepad", 1234);

    [Fact]
    public async Task RunAsync_HappyPath_RunsStepsInOrder()
    {
        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[enhanced] raw transcript", result.FinalText);
        Assert.Equal("raw transcript", result.RawTranscript);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(["transcribe", "llm", "gate", "history", "output"], _callOrder);
    }

    [Fact]
    public async Task RunAsync_HappyPath_HistoryAndOutputReceiveFinalText()
    {
        await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        _history.Verify(h => h.AddAsync(It.IsAny<DateTime>(), "[enhanced] raw transcript", It.IsAny<CancellationToken>()), Times.Once);
        _output.Verify(o => o.OutputAsync("[enhanced] raw transcript"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_UsesActivePromptModeFromSettings()
    {
        _appSettings.ActivePromptMode = "Email";

        await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        _resolver.Verify(r => r.Resolve("raw transcript", "Email"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_RewindsSeekableAudioBeforeTranscribing()
    {
        var request = CreateRequest();
        request.Audio.Position = request.Audio.Length; // as left behind by the recorder
        long observedPosition = -1;
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((audio, _) => observedPosition = audio.Position)
            .ReturnsAsync(new TranscriptionResult("raw transcript", null, null));

        await CreatePipeline().RunAsync(request, CancellationToken.None);

        Assert.Equal(0, observedPosition);
    }

    [Fact]
    public async Task RunAsync_TranscriptionFails_ReturnsFailureWithoutGateHistoryOrOutput()
    {
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Speech", "bad key", isConfigurationError: true));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.FinalText);
        Assert.Contains("bad key", result.ErrorMessage);
        Assert.Contains("settings", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _gate.Verify(g => g.ConfirmAsync(It.IsAny<PipelineResult>()), Times.Never);
        _history.Verify(h => h.AddAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_UnexpectedTranscriptionException_BecomesFailedResultNotThrow()
    {
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_LlmFails_GateOfferedRawTranscriptWithWarning()
    {
        _llm.Setup(l => l.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("AzureFoundryLLM", "model overloaded"));
        PipelineResult? draft = null;
        _gate.Setup(g => g.ConfirmAsync(It.IsAny<PipelineResult>()))
            .Callback<PipelineResult>(d => draft = d)
            .ReturnsAsync((PipelineResult d) => d.FinalText);

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        // The dictation is not lost: the raw transcript flows through, flagged for the gate.
        Assert.NotNull(draft);
        Assert.Equal("raw transcript", draft!.FinalText);
        Assert.Contains("model overloaded", draft.ErrorMessage);
        Assert.True(result.Success);
        Assert.Equal("raw transcript", result.FinalText);
        _output.Verify(o => o.OutputAsync("raw transcript"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ResolverThrows_FallsBackToRawTranscript()
    {
        _resolver.Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("boom"));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("raw transcript", result.FinalText);
    }

    [Fact]
    public async Task RunAsync_GateReturnsNull_CancelledWithoutHistoryOrOutput()
    {
        _gate.Setup(g => g.ConfirmAsync(It.IsAny<PipelineResult>())).ReturnsAsync((string?)null);

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        // Cancelled is success-without-text: no error surfaces, nothing is written or delivered.
        Assert.True(result.Success);
        Assert.Null(result.FinalText);
        Assert.Null(result.ErrorMessage);
        _history.Verify(h => h.AddAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_GateReturnsEditedText_HistoryAndOutputReceiveTheEdit()
    {
        _gate.Setup(g => g.ConfirmAsync(It.IsAny<PipelineResult>())).ReturnsAsync("edited text");

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("edited text", result.FinalText);
        _history.Verify(h => h.AddAsync(It.IsAny<DateTime>(), "edited text", It.IsAny<CancellationToken>()), Times.Once);
        _output.Verify(o => o.OutputAsync("edited text"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_GateThrows_BecomesFailedResult()
    {
        _gate.Setup(g => g.ConfirmAsync(It.IsAny<PipelineResult>()))
            .ThrowsAsync(new InvalidOperationException("dialog exploded"));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("dialog exploded", result.ErrorMessage);
        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_HistoryThrows_OutputStillDelivered()
    {
        _history.Setup(h => h.AddAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        _output.Verify(o => o.OutputAsync("[enhanced] raw transcript"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_OutputThrows_FailedResultKeepsFinalText()
    {
        _output.Setup(o => o.OutputAsync(It.IsAny<string>()))
            .ThrowsAsync(new ProviderException("ClipboardPaste", "clipboard locked"));

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("[enhanced] raw transcript", result.FinalText); // the text is not lost
        Assert.Contains("clipboard locked", result.ErrorMessage);
        _history.Verify(h => h.AddAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SelectsOutputProviderFromSettingsPerCall()
    {
        var keyboard = new Mock<IOutputProvider>();
        keyboard.SetupGet(o => o.Name).Returns(OutputProviderNames.SimulatedKeyboard);
        keyboard.Setup(o => o.OutputAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pipeline = CreatePipeline(_output.Object, keyboard.Object);

        _appSettings.Output.Provider = OutputProviderNames.SimulatedKeyboard;
        await pipeline.RunAsync(CreateRequest(), CancellationToken.None);

        keyboard.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Once);
        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Never);

        // Settings are read per run — switching applies to the very next dictation.
        _appSettings.Output.Provider = OutputProviderNames.ClipboardPaste;
        await pipeline.RunAsync(CreateRequest(), CancellationToken.None);

        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_UnknownOutputProviderName_FallsBackToFirstRegistered()
    {
        _appSettings.Output.Provider = "DoesNotExist";

        var result = await CreatePipeline().RunAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        _output.Verify(o => o.OutputAsync(It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// End-to-end without network or UI: mock speech, a real resolver (store seeded into a
    /// temp dir), mock LLM, a real SQLite history repository and a pass-through gate — a
    /// full dictation lands in the output provider and the History table.
    /// </summary>
    [Fact]
    public async Task RunAsync_MockProvidersRealResolverAndHistory_EndToEnd()
    {
        using var paths = new TestAppPaths();
        await new DatabaseInitializer(paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        _appSettings.ActivePromptMode = "Raw";

        var speech = new MockTranscriptionProvider { CannedText = "hello world", Delay = TimeSpan.Zero };
        var store = new PromptModeStore(paths, NullLogger<PromptModeStore>.Instance);
        var foregroundApp = new Mock<IForegroundAppService>();
        foregroundApp.SetupGet(f => f.LastCaptured).Returns("notepad");
        var resolver = new PromptResolver(
            store, _settings.Object, foregroundApp.Object, TimeProvider.System, NullLogger<PromptResolver>.Instance);
        var llm = new MockLLMProvider { Delay = TimeSpan.Zero };
        var history = new SqliteHistoryRepository(paths, _settings.Object, NullLogger<SqliteHistoryRepository>.Instance);

        var pipeline = new DictationPipeline(
            speech, resolver, llm, history, [_output.Object], _gate.Object,
            _settings.Object, TimeProvider.System, Mock.Of<ILogger<DictationPipeline>>());

        var result = await pipeline.RunAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[enhanced] hello world", result.FinalText);
        Assert.Equal("hello world", result.RawTranscript);
        _output.Verify(o => o.OutputAsync("[enhanced] hello world"), Times.Once);
        Assert.Equal(["[enhanced] hello world"], await HistoryRepositoryTests.ReadFinalTextsAsync(paths));
    }
}
