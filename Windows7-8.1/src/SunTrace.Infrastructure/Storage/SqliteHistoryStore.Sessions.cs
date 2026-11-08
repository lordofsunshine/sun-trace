using System.Globalization;
using Microsoft.Data.Sqlite;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<long> CreateSessionAsync(TrackingSession session, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (started_at, completed_at, baseline_snapshot_id, installer_path, status, summary, new_app_count, event_count)
            VALUES ($started, $completed, $baseline, $installer, $status, $summary, $newApps, $events)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$started", DbTime.ToDb(session.StartedAt));
        command.Parameters.AddWithValue("$completed", session.CompletedAt is null ? DBNull.Value : DbTime.ToDb(session.CompletedAt.Value));
        command.Parameters.AddWithValue("$baseline", session.BaselineSnapshotId is null ? DBNull.Value : session.BaselineSnapshotId.Value);
        command.Parameters.AddWithValue("$installer", (object?)session.InstallerPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$summary", (object?)session.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$newApps", session.NewAppCount);
        command.Parameters.AddWithValue("$events", session.EventCount);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        session.Id = id;
        return id;
    }

    public async Task UpdateSessionAsync(TrackingSession session, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions SET completed_at = $completed, installer_path = $installer, status = $status, summary = $summary,
                                new_app_count = $newApps, event_count = $events
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$completed", session.CompletedAt is null ? DBNull.Value : DbTime.ToDb(session.CompletedAt.Value));
        command.Parameters.AddWithValue("$installer", (object?)session.InstallerPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$summary", (object?)session.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$newApps", session.NewAppCount);
        command.Parameters.AddWithValue("$events", session.EventCount);
        command.Parameters.AddWithValue("$id", session.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackingSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, started_at, completed_at, baseline_snapshot_id, installer_path, status, summary, new_app_count, event_count
            FROM sessions
            WHERE status IN (0, 1, 2, 5)
            ORDER BY id DESC LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TrackingSession
        {
            Id = reader.GetInt64(0),
            StartedAt = DbTime.FromDb(reader.GetString(1)) ?? DateTimeOffset.MinValue,
            CompletedAt = reader.IsDBNull(2) ? null : DbTime.FromDb(reader.GetString(2)),
            BaselineSnapshotId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
            InstallerPath = reader.IsDBNull(4) ? null : reader.GetString(4),
            Status = (TrackingSessionStatus)reader.GetInt32(5),
            Summary = reader.IsDBNull(6) ? null : reader.GetString(6),
            NewAppCount = reader.GetInt32(7),
            EventCount = reader.GetInt32(8)
        };
    }
}
