using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<int> PruneEventsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events WHERE detected_at < $threshold;";
        command.Parameters.AddWithValue("$threshold", DbTime.ToDb(olderThan));
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            _logger.LogInformation("Удалено устаревших событий: {Count}.", deleted);
        }

        return deleted;
    }

    public async Task<int> AppendEventsAsync(IEnumerable<ChangeEvent> events, TimeSpan deduplicationWindow, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var list = events.Where(e => !string.IsNullOrWhiteSpace(e.Title)).ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        if (deduplicationWindow > TimeSpan.Zero)
        {
            var threshold = DbTime.ToDb(DateTimeOffset.Now - deduplicationWindow);
            foreach (var chunk in list.Select(e => e.Fingerprint).Distinct().Chunk(300))
            {
                await using var query = connection.CreateCommand();
                var names = new List<string>();
                for (var i = 0; i < chunk.Length; i++)
                {
                    names.Add("$f" + i);
                    query.Parameters.AddWithValue("$f" + i, chunk[i]);
                }

                query.CommandText = $"SELECT fingerprint FROM events WHERE detected_at >= $threshold AND fingerprint IN ({string.Join(',', names)});";
                query.Parameters.AddWithValue("$threshold", threshold);
                await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existing.Add(reader.GetString(0));
                }
            }
        }

        var inserted = 0;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO events (fingerprint, app_id, app_name, kind, component_type, component_name, title, description,
                                details_json, confidence, confidence_reasons_json, importance, source, detected_at,
                                snapshot_id, session_id, is_reverted, is_read, affected_count)
            VALUES ($fingerprint, $appId, $appName, $kind, $componentType, $componentName, $title, $description,
                    $details, $confidence, $reasons, $importance, $source, $detectedAt, $snapshotId, $sessionId, 0, 0, $affected);
            """;
        foreach (var name in new[]
                 {
                     "$fingerprint", "$appId", "$appName", "$kind", "$componentType", "$componentName", "$title", "$description",
                     "$details", "$confidence", "$reasons", "$importance", "$source", "$detectedAt", "$snapshotId", "$sessionId", "$affected"
                 })
        {
            AddParameter(insert, name, null);
        }

        foreach (var item in list)
        {
            if (!string.IsNullOrEmpty(item.Fingerprint) && !existing.Add(item.Fingerprint))
            {
                continue;
            }

            insert.Parameters["$fingerprint"].Value = item.Fingerprint;
            insert.Parameters["$appId"].Value = (object?)item.AppId ?? DBNull.Value;
            insert.Parameters["$appName"].Value = item.AppName ?? string.Empty;
            insert.Parameters["$kind"].Value = (int)item.Kind;
            insert.Parameters["$componentType"].Value = (int)item.ComponentType;
            insert.Parameters["$componentName"].Value = item.ComponentName ?? string.Empty;
            insert.Parameters["$title"].Value = item.Title;
            insert.Parameters["$description"].Value = item.Description;
            insert.Parameters["$details"].Value = StorageJson.Serialize(item.Details);
            insert.Parameters["$confidence"].Value = (int)item.Confidence;
            insert.Parameters["$reasons"].Value = StorageJson.Serialize(item.ConfidenceReasons);
            insert.Parameters["$importance"].Value = (int)item.Importance;
            insert.Parameters["$source"].Value = item.Source ?? string.Empty;
            insert.Parameters["$detectedAt"].Value = DbTime.ToDb(item.DetectedAt);
            insert.Parameters["$snapshotId"].Value = item.SnapshotId is null ? DBNull.Value : item.SnapshotId.Value;
            insert.Parameters["$sessionId"].Value = item.SessionId is null ? DBNull.Value : item.SessionId.Value;
            insert.Parameters["$affected"].Value = item.AffectedCount;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            inserted++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<EventPage> QueryEventsAsync(EventQuery query, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var where = new List<string>();
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(query.AppId))
        {
            where.Add("app_id = $appId");
            command.Parameters.AddWithValue("$appId", query.AppId);
        }

        if (query.ComponentType is not null)
        {
            where.Add("component_type = $componentType");
            command.Parameters.AddWithValue("$componentType", (int)query.ComponentType.Value);
        }

        if (query.Importance is not null)
        {
            where.Add("importance >= $importance");
            command.Parameters.AddWithValue("$importance", (int)query.Importance.Value);
        }

        if (query.Kind is not null)
        {
            where.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", (int)query.Kind.Value);
        }

        if (query.From is not null)
        {
            where.Add("detected_at >= $from");
            command.Parameters.AddWithValue("$from", DbTime.ToDb(query.From.Value));
        }

        if (query.To is not null)
        {
            where.Add("detected_at <= $to");
            command.Parameters.AddWithValue("$to", DbTime.ToDb(query.To.Value));
        }

        if (query.SnapshotId is not null)
        {
            where.Add("snapshot_id = $snapshotId");
            command.Parameters.AddWithValue("$snapshotId", query.SnapshotId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            where.Add("(title LIKE $term ESCAPE '\\' OR description LIKE $term ESCAPE '\\' OR app_name LIKE $term ESCAPE '\\' OR component_name LIKE $term ESCAPE '\\')");
            command.Parameters.AddWithValue("$term", "%" + EscapeLike(query.SearchTerm.Trim()) + "%");
        }

        var whereSql = where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);
        command.CommandText = $"""
            SELECT COUNT(*) FROM events {whereSql};
            """;
        var total = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        command.CommandText = $"""
            SELECT id, fingerprint, app_id, app_name, kind, component_type, component_name, title, description,
                   details_json, confidence, confidence_reasons_json, importance, source, detected_at, snapshot_id,
                   session_id, is_reverted, is_read, affected_count
            FROM events {whereSql}
            ORDER BY detected_at DESC, id DESC
            LIMIT $take OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$take", Math.Clamp(query.PageSize, 1, 500));
        command.Parameters.AddWithValue("$offset", Math.Max(0, query.Offset));

        var items = new List<ChangeEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadEvent(reader));
        }

        return new EventPage { Items = items, TotalCount = total, Offset = Math.Max(0, query.Offset) };
    }

    public async Task<ChangeEvent?> GetEventAsync(long id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, fingerprint, app_id, app_name, kind, component_type, component_name, title, description,
                   details_json, confidence, confidence_reasons_json, importance, source, detected_at, snapshot_id,
                   session_id, is_reverted, is_read, affected_count
            FROM events WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    public async Task<IReadOnlyList<ChangeEvent>> GetAppEventsAsync(string appId, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var page = await QueryEventsAsync(new EventQuery { AppId = appId, Offset = offset, PageSize = limit }, cancellationToken).ConfigureAwait(false);
        return page.Items;
    }

    public async Task<int> GetAppEventCountAsync(string appId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE app_id = $appId;";
        command.Parameters.AddWithValue("$appId", appId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task MarkAppEventsReadAsync(string appId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE events SET is_read = 1 WHERE app_id = $appId AND is_read = 0;";
        command.Parameters.AddWithValue("$appId", appId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ChangeEvent ReadEvent(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Fingerprint = reader.GetString(1),
        AppId = reader.IsDBNull(2) ? null : reader.GetString(2),
        AppName = reader.GetString(3),
        Kind = (ChangeKind)reader.GetInt32(4),
        ComponentType = (ComponentType)reader.GetInt32(5),
        ComponentName = reader.GetString(6),
        Title = reader.GetString(7),
        Description = reader.GetString(8),
        Details = StorageJson.Deserialize<EventDetails>(reader.IsDBNull(9) ? null : reader.GetString(9)) ?? new EventDetails(),
        Confidence = (ConfidenceLevel)reader.GetInt32(10),
        ConfidenceReasons = StorageJson.Deserialize<List<string>>(reader.IsDBNull(11) ? null : reader.GetString(11)) ?? new List<string>(),
        Importance = (ImportanceLevel)reader.GetInt32(12),
        Source = reader.GetString(13),
        DetectedAt = DbTime.FromDb(reader.GetString(14)) ?? DateTimeOffset.MinValue,
        SnapshotId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
        SessionId = reader.IsDBNull(16) ? null : reader.GetInt64(16),
        IsReverted = reader.GetInt32(17) == 1,
        IsRead = reader.GetInt32(18) == 1,
        AffectedCount = reader.GetInt32(19)
    };
}
