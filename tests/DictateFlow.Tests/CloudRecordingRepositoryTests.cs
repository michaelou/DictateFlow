using DictateFlow.Core.Services;
using DictateFlow.Core.Services.CloudRecordings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>Tests for <see cref="SqliteCloudRecordingRepository"/> against a temp-file SQLite database.</summary>
public sealed class CloudRecordingRepositoryTests : IDisposable
{
    private readonly TestAppPaths _paths = new();
    private readonly SqliteCloudRecordingRepository _repository;

    public CloudRecordingRepositoryTests()
    {
        _repository = new SqliteCloudRecordingRepository(_paths, NullLogger<SqliteCloudRecordingRepository>.Instance);

        // The repository writes into the schema the initializer bootstraps at app startup.
        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        // Release pooled connections so the temp directory can be deleted.
        SqliteConnection.ClearAllPools();
        _paths.Dispose();
    }

    [Fact]
    public async Task AddAsync_ThenGetAll_RoundTripsTheEntry()
    {
        var modified = new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc);
        var transcribed = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);

        await _repository.AddAsync("audio/clip1.m4a", modified, transcribed, "Hello café 你好", 12.5);

        var entry = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("audio/clip1.m4a", entry.BlobName);
        Assert.Equal(modified, entry.LastModifiedUtc);
        Assert.Equal(transcribed, entry.TranscribedUtc);
        Assert.Equal(DateTimeKind.Utc, entry.TranscribedUtc.Kind);
        Assert.Equal("Hello café 你好", entry.Transcript);
        Assert.Equal(12.5, entry.DurationSeconds);
        Assert.True(entry.Id > 0);
    }

    [Fact]
    public async Task AddAsync_NullOptionalFields_StoredAsNull()
    {
        await _repository.AddAsync("clip.m4a", lastModifiedUtc: null, DateTime.UtcNow, "text", durationSeconds: null);

        var entry = Assert.Single(await _repository.GetAllAsync());
        Assert.Null(entry.LastModifiedUtc);
        Assert.Null(entry.DurationSeconds);
    }

    [Fact]
    public async Task AddAsync_DuplicateBlobName_IsIgnored()
    {
        await _repository.AddAsync("dup.m4a", null, DateTime.UtcNow, "first", null);
        await _repository.AddAsync("dup.m4a", null, DateTime.UtcNow, "second", null);

        var entry = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("first", entry.Transcript);
    }

    [Fact]
    public async Task GetProcessedBlobNames_ReturnsEveryStoredBlobName()
    {
        await _repository.AddAsync("a.m4a", null, DateTime.UtcNow, "a", null);
        await _repository.AddAsync("b.m4a", null, DateTime.UtcNow, "b", null);

        var names = await _repository.GetProcessedBlobNamesAsync();

        Assert.Equal(["a.m4a", "b.m4a"], names.OrderBy(n => n));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNewestFirst()
    {
        await _repository.AddAsync("first.m4a", null, DateTime.UtcNow, "first", null);
        await _repository.AddAsync("second.m4a", null, DateTime.UtcNow, "second", null);
        await _repository.AddAsync("third.m4a", null, DateTime.UtcNow, "third", null);

        var transcripts = (await _repository.GetAllAsync()).Select(e => e.Transcript);

        Assert.Equal(["third", "second", "first"], transcripts);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheGivenEntry()
    {
        await _repository.AddAsync("keep.m4a", null, DateTime.UtcNow, "keep", null);
        await _repository.AddAsync("drop.m4a", null, DateTime.UtcNow, "drop", null);
        var target = (await _repository.GetAllAsync()).Single(e => e.BlobName == "drop.m4a");

        await _repository.DeleteAsync(target.Id);

        var remaining = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("keep.m4a", remaining.BlobName);
    }
}
