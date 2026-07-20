using System.Globalization;
using DictateFlow.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.CloudRecordings;

/// <summary>
/// Default <see cref="ICloudRecordingRepository"/> implementation writing to the
/// <c>CloudRecordings</c> table bootstrapped by <see cref="IDatabaseInitializer"/>. Each call
/// opens its own short-lived connection, mirroring <c>SqliteHistoryRepository</c>. Timestamps
/// are stored as ISO-8601 UTC strings.
/// </summary>
public sealed class SqliteCloudRecordingRepository : ICloudRecordingRepository
{
    private readonly IAppPaths _appPaths;
    private readonly ILogger<SqliteCloudRecordingRepository> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteCloudRecordingRepository"/> class.</summary>
    /// <param name="appPaths">Resolves the location of the database file.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SqliteCloudRecordingRepository(IAppPaths appPaths, ILogger<SqliteCloudRecordingRepository> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetProcessedBlobNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT BlobName FROM CloudRecordings;";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <inheritdoc />
    public async Task AddAsync(
        string blobName,
        DateTime? lastModifiedUtc,
        DateTime transcribedUtc,
        string transcript,
        double? durationSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // INSERT OR IGNORE keeps AddAsync idempotent: the UNIQUE BlobName means a re-run of the
        // same blob is silently dropped rather than throwing.
        command.CommandText =
            "INSERT OR IGNORE INTO CloudRecordings (BlobName, LastModifiedUtc, TranscribedUtc, Transcript, DurationSeconds) "
            + "VALUES ($blob, $modified, $transcribed, $transcript, $duration);";
        command.Parameters.AddWithValue("$blob", blobName);
        command.Parameters.AddWithValue("$modified", (object?)ToIso(lastModifiedUtc) ?? DBNull.Value);
        command.Parameters.AddWithValue("$transcribed", ToIso(transcribedUtc)!);
        command.Parameters.AddWithValue("$transcript", transcript);
        command.Parameters.AddWithValue("$duration", (object?)durationSeconds ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Cloud recording '{BlobName}' persisted ({CharCount} characters)", blobName, transcript.Length);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudRecordingEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, BlobName, LastModifiedUtc, TranscribedUtc, Transcript, DurationSeconds "
            + "FROM CloudRecordings ORDER BY Id DESC;";

        var entries = new List<CloudRecordingEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new CloudRecordingEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : ParseUtc(reader.GetString(2)),
                ParseUtc(reader.GetString(3)),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5)));
        }

        _logger.LogDebug("Cloud recordings loaded ({Count} entries)", entries.Count);
        return entries;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudRecordings WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Cloud recording {Id} deleted", id);
    }

    /// <summary>Opens a connection to the database file (schema is created by <see cref="IDatabaseInitializer"/>).</summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string? ToIso(DateTime? value)
        => value is null
            ? null
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
