using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.CloudRecordings;
using DictateFlow.Core.Services.History;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="CloudTranscriptionService"/> with fake blob source, decoder and
/// transcription provider, against a real temp-file repository. The fakes carry the blob name
/// through the download → decode → transcribe chain (as the file content) so transcripts can be
/// mapped back to their blob.
/// </summary>
public sealed class CloudTranscriptionServiceTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly SqliteCloudRecordingRepository _repository;
    private readonly FakeRecordingSource _source = new();
    private readonly FakeAudioDecoder _decoder = new();
    private readonly FakeTranscriptionProvider _transcription = new();
    private readonly Mock<IPromptResolver> _promptResolver = new();
    private readonly Mock<ILLMProvider> _llmProvider = new();
    private readonly Mock<IHistoryRepository> _history = new();
    private readonly Mock<IUsageSink> _usageSink = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly AppSettings _settings = new();
    private readonly CloudTranscriptionService _service;

    public CloudTranscriptionServiceTests()
    {
        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteCloudRecordingRepository(_paths, NullLogger<SqliteCloudRecordingRepository>.Instance);
        _settingsService.SetupGet(s => s.Current).Returns(_settings);
        _service = new CloudTranscriptionService(
            _source, _repository, _decoder, _transcription,
            _promptResolver.Object, _llmProvider.Object, _history.Object, _usageSink.Object,
            _settingsService.Object, TimeProvider.System,
            NullLogger<CloudTranscriptionService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _paths.Dispose();
    }

    [Fact]
    public async Task CheckAndTranscribeNew_TranscribesOnlyUnprocessedBlobs()
    {
        await _repository.AddAsync("old.m4a", null, DateTime.UtcNow, "already done", null);
        _source.Blobs = [Blob("old.m4a"), Blob("new1.m4a"), Blob("new2.m4a")];

        var count = await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, _transcription.CallCount); // "old.m4a" was skipped
        var stored = await _repository.GetAllAsync();
        Assert.Contains(stored, e => e.BlobName == "new1.m4a" && e.Transcript == "transcript of new1.m4a");
        Assert.Contains(stored, e => e.BlobName == "new2.m4a" && e.Transcript == "transcript of new2.m4a");
        Assert.Equal(3, stored.Count);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_NothingNew_ReturnsZero()
    {
        await _repository.AddAsync("only.m4a", null, DateTime.UtcNow, "done", null);
        _source.Blobs = [Blob("only.m4a")];

        var count = await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(0, _transcription.CallCount);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_OneTranscriptionFails_OthersStillProcessed()
    {
        _source.Blobs = [Blob("good1.m4a"), Blob("bad.m4a"), Blob("good2.m4a")];
        _transcription.FailForContent = "bad.m4a";

        var count = await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        Assert.Equal(2, count);
        var stored = await _repository.GetAllAsync();
        Assert.Equal(2, stored.Count);
        Assert.DoesNotContain(stored, e => e.BlobName == "bad.m4a");
    }

    [Fact]
    public async Task CheckAndTranscribeNew_OneDecodeFails_OthersStillProcessed()
    {
        _source.Blobs = [Blob("good.m4a"), Blob("undecodable.m4a")];
        _decoder.FailForContent = "undecodable.m4a";

        var count = await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        Assert.Equal(1, count);
        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("good.m4a", stored.BlobName);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_StoresInHistoryAndRecordsUsage()
    {
        _source.Blobs = [Blob("clip.m4a")];

        await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        _history.Verify(
            h => h.AddAsync(
                It.IsAny<DateTime>(), "transcript of clip.m4a", "transcript of clip.m4a", null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _usageSink.Verify(
            u => u.Record(It.Is<UsageRecord>(r =>
                r.Category == UsageCategories.Dictation && r.WordCount == 3)),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_PromptModeConfigured_StoresEnhancedTranscript()
    {
        _settings.CloudRecordings.PromptMode = "Email";
        _promptResolver
            .Setup(r => r.Resolve("transcript of clip.m4a", "Email"))
            .Returns(new PromptContext("system", "transcript of clip.m4a", 0.2, 1000, "Email"));
        _llmProvider
            .Setup(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("enhanced email body");
        _source.Blobs = [Blob("clip.m4a")];

        await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("enhanced email body", stored.Transcript);
        // History keeps the enhanced final text and the raw transcript, tagged with the mode.
        _history.Verify(
            h => h.AddAsync(
                It.IsAny<DateTime>(), "enhanced email body", "transcript of clip.m4a", "Email",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_EnhancementFails_FallsBackToRawTranscript()
    {
        _settings.CloudRecordings.PromptMode = "Email";
        _promptResolver
            .Setup(r => r.Resolve(It.IsAny<string>(), "Email"))
            .Returns(new PromptContext("system", "transcript of clip.m4a", 0.2, 1000, "Email"));
        _llmProvider
            .Setup(p => p.ProcessAsync(It.IsAny<PromptContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Mock", "llm down"));
        _source.Blobs = [Blob("clip.m4a")];

        var count = await _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None);

        Assert.Equal(1, count);
        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("transcript of clip.m4a", stored.Transcript);
    }

    [Fact]
    public async Task CheckAndTranscribeNew_ListingFails_Throws()
    {
        _source.ThrowOnList = new ProviderException("AzureBlobStorage", "bad config", isConfigurationError: true);

        await Assert.ThrowsAsync<ProviderException>(
            () => _service.CheckAndTranscribeNewAsync(progress: null, CancellationToken.None));
    }

    private static CloudRecordingBlob Blob(string name) => new(name, DateTime.UtcNow, 1024, "audio/mp4");

    /// <summary>Fake source that carries the blob name through as the downloaded file content.</summary>
    private sealed class FakeRecordingSource : ICloudRecordingSource
    {
        public IReadOnlyList<CloudRecordingBlob> Blobs { get; set; } = [];

        public ProviderException? ThrowOnList { get; set; }

        public Task<IReadOnlyList<CloudRecordingBlob>> ListAsync(CancellationToken cancellationToken)
            => ThrowOnList is not null ? throw ThrowOnList : Task.FromResult(Blobs);

        public async Task DownloadToFileAsync(string blobName, string destinationPath, CancellationToken cancellationToken)
            => await File.WriteAllTextAsync(destinationPath, blobName, cancellationToken);

        public Task DeleteAsync(string blobName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Fake decoder that copies the (name-carrying) content to the WAV path.</summary>
    private sealed class FakeAudioDecoder : IAudioDecoder
    {
        public string? FailForContent { get; set; }

        public async Task DecodeToWav16kMonoAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken)
        {
            var content = await File.ReadAllTextAsync(inputPath, cancellationToken);
            if (content == FailForContent)
            {
                throw new InvalidOperationException("cannot decode");
            }

            await File.WriteAllTextAsync(outputWavPath, content, cancellationToken);
        }
    }

    /// <summary>Fake provider that returns "transcript of {content}", or fails for a configured content.</summary>
    private sealed class FakeTranscriptionProvider : ITranscriptionProvider
    {
        public int CallCount { get; private set; }

        public string? FailForContent { get; set; }

        public async Task<TranscriptionResult> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
        {
            CallCount++;
            using var reader = new StreamReader(audio);
            var content = await reader.ReadToEndAsync(cancellationToken);
            if (content == FailForContent)
            {
                throw new ProviderException("Mock", "transcription failed");
            }

            return new TranscriptionResult($"transcript of {content}", 1.0, "en-US");
        }
    }
}
