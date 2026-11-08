using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<SystemSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, captured_at, reason, payload FROM snapshots ORDER BY id DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<SnapshotStamp?> GetLatestSnapshotStampAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadLatestStampAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemSnapshot?> GetSnapshotAsync(long id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, captured_at, reason, payload FROM snapshots WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<IReadOnlyList<SnapshotSummary>> GetSnapshotsAsync(int limit = 120, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, captured_at, reason, app_count, component_count, is_complete
            FROM snapshots ORDER BY id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var result = new List<SnapshotSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new SnapshotSummary
            {
                Id = reader.GetInt64(0),
                CapturedAt = DbTime.FromDb(reader.GetString(1)) ?? DateTimeOffset.MinValue,
                Reason = (ScanReason)reader.GetInt32(2),
                AppCount = reader.GetInt32(3),
                ComponentCount = reader.GetInt32(4),
                IsComplete = reader.GetInt32(5) == 1
            });
        }

        return result;
    }

    public async Task PruneSnapshotsAsync(int keep, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM snapshots
            WHERE id NOT IN (SELECT id FROM snapshots ORDER BY id DESC LIMIT $keep)
              AND id NOT IN (SELECT baseline_snapshot_id FROM sessions
                             WHERE baseline_snapshot_id IS NOT NULL AND status IN (0, 1, 2, 5));
            """;
        command.Parameters.AddWithValue("$keep", Math.Max(5, keep));
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            _logger.LogInformation("Удалено старых снимков: {Count}.", deleted);
        }
    }

    private static byte[] SerializeContent(SystemSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new SystemSnapshot
            {
                CapturedAt = default,
                Reason = default,
                Apps = snapshot.Apps,
                Components = snapshot.Components,
                Sections = snapshot.Sections,
                SessionId = snapshot.SessionId
            },
            StorageJson.Options);

    private static async Task<SnapshotStamp?> ReadLatestStampAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, payload_hash FROM snapshots ORDER BY id DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new SnapshotStamp(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    private static async Task TouchSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, long snapshotId, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE snapshots SET captured_at = $captured WHERE id = $id;
            UPDATE apps SET last_seen = $now WHERE last_seen = (SELECT MAX(last_seen) FROM apps);
            UPDATE components SET last_seen = $now WHERE last_seen = (SELECT MAX(last_seen) FROM components);
            """;
        command.Parameters.AddWithValue("$captured", DbTime.ToDb(capturedAt));
        command.Parameters.AddWithValue("$id", snapshotId);
        command.Parameters.AddWithValue("$now", DbTime.Now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] Compress(byte[] json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return output.ToArray();
    }

    private static SystemSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        var payload = (byte[])reader.GetValue(3);
        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var snapshot = JsonSerializer.Deserialize<SystemSnapshot>(gzip, StorageJson.Options) ?? new SystemSnapshot();
        snapshot.Id = reader.GetInt64(0);
        snapshot.CapturedAt = DbTime.FromDb(reader.GetString(1)) ?? snapshot.CapturedAt;
        snapshot.Reason = (ScanReason)reader.GetInt32(2);
        return snapshot;
    }
}
