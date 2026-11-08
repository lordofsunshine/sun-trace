using Microsoft.Data.Sqlite;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<IReadOnlyList<AppSummary>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var apps = await LoadAppRowsAsync(connection, presentOnly: true, cancellationToken).ConfigureAwait(false);
        return await BuildSummariesAsync(connection, apps, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSummary?> GetAppAsync(string appId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var apps = await LoadAppRowsAsync(connection, presentOnly: false, cancellationToken).ConfigureAwait(false);
        var target = apps.FirstOrDefault(a => a.Id == appId);
        if (target is null)
        {
            return null;
        }

        var summaries = await BuildSummariesAsync(connection, [target], cancellationToken).ConfigureAwait(false);
        return summaries.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ComponentSnapshot>> GetAppComponentsAsync(string appId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, name, detail, executable_path, publisher, signature_publisher, enabled, start_mode, state,
                   owner, location, size_bytes, modified_utc, is_microsoft, is_security_sensitive, supports_pause,
                   pause_json, details_json
            FROM components WHERE app_id = $appId AND status = 'active'
            ORDER BY type, name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$appId", appId);
        var result = new List<ComponentSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadComponent(reader));
        }

        return result;
    }

    public async Task<IReadOnlyList<ComponentSnapshot>> GetComponentsAsync(ComponentType? type, string? search, int limit, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var where = new List<string> { "status = 'active'" };
        if (type is not null)
        {
            where.Add("type = $type");
            command.Parameters.AddWithValue("$type", (int)type.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(name LIKE $term ESCAPE '\\' OR detail LIKE $term ESCAPE '\\')");
            command.Parameters.AddWithValue("$term", "%" + EscapeLike(search.Trim()) + "%");
        }

        command.CommandText = $"""
            SELECT id, type, name, detail, executable_path, publisher, signature_publisher, enabled, start_mode, state,
                   owner, location, size_bytes, modified_utc, is_microsoft, is_security_sensitive, supports_pause,
                   pause_json, details_json
            FROM components WHERE {string.Join(" AND ", where)}
            ORDER BY type, name COLLATE NOCASE LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        var result = new List<ComponentSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadComponent(reader));
        }

        return result;
    }

    private async Task<List<InstalledApp>> LoadAppRowsAsync(SqliteConnection connection, bool presentOnly, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = presentOnly
            ? """
              SELECT id, name, publisher, version, install_date, last_updated, install_location, uninstall_string, display_icon,
                     main_executable, registry_key, signature_publisher, estimated_size, is_system, source
              FROM apps WHERE present = 1 ORDER BY name COLLATE NOCASE;
              """
            : """
              SELECT id, name, publisher, version, install_date, last_updated, install_location, uninstall_string, display_icon,
                     main_executable, registry_key, signature_publisher, estimated_size, is_system, source
              FROM apps ORDER BY name COLLATE NOCASE;
              """;
        var apps = new List<InstalledApp>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            apps.Add(new InstalledApp
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Publisher = reader.IsDBNull(2) ? null : reader.GetString(2),
                Version = reader.IsDBNull(3) ? null : reader.GetString(3),
                InstallDate = reader.IsDBNull(4) ? null : DbTime.FromDb(reader.GetString(4)),
                LastUpdated = reader.IsDBNull(5) ? null : DbTime.FromDb(reader.GetString(5)),
                InstallLocation = reader.IsDBNull(6) ? null : reader.GetString(6),
                UninstallString = reader.IsDBNull(7) ? null : reader.GetString(7),
                DisplayIcon = reader.IsDBNull(8) ? null : reader.GetString(8),
                MainExecutablePath = reader.IsDBNull(9) ? null : reader.GetString(9),
                RegistryKey = reader.IsDBNull(10) ? null : reader.GetString(10),
                SignaturePublisher = reader.IsDBNull(11) ? null : reader.GetString(11),
                EstimatedSizeBytes = reader.IsDBNull(12) ? null : reader.GetInt64(12),
                IsSystemComponent = reader.GetInt32(13) == 1,
                Source = reader.IsDBNull(14) ? "Реестр" : reader.GetString(14)
            });
        }

        return apps;
    }

    private async Task<List<AppSummary>> BuildSummariesAsync(SqliteConnection connection, List<InstalledApp> apps, CancellationToken cancellationToken)
    {
        if (apps.Count == 0)
        {
            return [];
        }

        var week = DbTime.ToDb(DateTimeOffset.Now.AddDays(-7));
        var stats = new Dictionary<string, EventAggregate>(StringComparer.Ordinal);
        var paused = new Dictionary<string, int>(StringComparer.Ordinal);
        var components = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstSeen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT app_id,
                       MAX(detected_at) AS last_event,
                       MAX(CASE WHEN kind = 2 AND component_type = 0 THEN detected_at END) AS last_update,
                       MAX(CASE WHEN kind = 0 AND component_type = 0 THEN detected_at END) AS installed_at,
                       MAX(CASE WHEN component_type NOT IN (5, 6) THEN detected_at END) AS last_change,
                       SUM(CASE WHEN importance = 2 AND is_reverted = 0 AND is_read = 0 THEN 1 ELSE 0 END) AS attention,
                       SUM(CASE WHEN is_reverted = 0 AND is_read = 0 THEN 1 ELSE 0 END) AS unread,
                       COUNT(*) AS total,
                       SUM(CASE WHEN detected_at >= $week THEN 1 ELSE 0 END) AS recent
                FROM events WHERE app_id IS NOT NULL GROUP BY app_id;
                """;
            command.Parameters.AddWithValue("$week", week);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                stats[reader.GetString(0)] = new EventAggregate
                {
                    LastEvent = reader.IsDBNull(1) ? null : DbTime.FromDb(reader.GetString(1)),
                    LastUpdate = reader.IsDBNull(2) ? null : DbTime.FromDb(reader.GetString(2)),
                    InstalledAt = reader.IsDBNull(3) ? null : DbTime.FromDb(reader.GetString(3)),
                    LastChange = reader.IsDBNull(4) ? null : DbTime.FromDb(reader.GetString(4)),
                    Attention = reader.GetInt32(5),
                    Unread = reader.GetInt32(6),
                    Total = reader.GetInt32(7),
                    Recent = reader.GetInt32(8)
                };
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT app_id, COUNT(*) FROM paused_components WHERE status = 0 AND app_id IS NOT NULL GROUP BY app_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                paused[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT app_id, COUNT(*) FROM components WHERE status = 'active' AND app_id IS NOT NULL GROUP BY app_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                components[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, first_seen FROM apps;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var seen = DbTime.FromDb(reader.GetString(1));
                if (seen is not null)
                {
                    firstSeen[reader.GetString(0)] = seen.Value;
                }
            }
        }

        var now = DateTimeOffset.Now;
        var result = new List<AppSummary>(apps.Count);
        foreach (var app in apps)
        {
            stats.TryGetValue(app.Id, out var aggregate);
            paused.TryGetValue(app.Id, out var pausedCount);
            components.TryGetValue(app.Id, out var componentCount);
            firstSeen.TryGetValue(app.Id, out var seen);

            var isNew = aggregate?.InstalledAt is not null && now - aggregate.InstalledAt.Value <= TimeSpan.FromDays(7);
            var status = AppStatusResolver.Resolve(now, aggregate?.LastUpdate, aggregate?.LastChange, aggregate?.Attention ?? 0, aggregate?.Unread ?? 0, pausedCount, isNew);

            result.Add(new AppSummary
            {
                App = app,
                FirstSeen = seen == default ? now : seen,
                LastSeen = now,
                LastEventAt = aggregate?.LastEvent,
                LastUpdateAt = aggregate?.LastUpdate,
                LastChangeAt = aggregate?.LastChange,
                ComponentCount = componentCount,
                PausedCount = pausedCount,
                AttentionCount = aggregate?.Attention ?? 0,
                UnreadCount = aggregate?.Unread ?? 0,
                EventCount = aggregate?.Total ?? 0,
                RecentChangeCount = aggregate?.Recent ?? 0,
                Status = status
            });
        }

        return result;
    }

    private static ComponentSnapshot ReadComponent(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Type = (ComponentType)reader.GetInt32(1),
        Name = reader.GetString(2),
        Detail = reader.IsDBNull(3) ? null : reader.GetString(3),
        ExecutablePath = reader.IsDBNull(4) ? null : reader.GetString(4),
        Publisher = reader.IsDBNull(5) ? null : reader.GetString(5),
        SignaturePublisher = reader.IsDBNull(6) ? null : reader.GetString(6),
        Enabled = reader.GetInt32(7) == 1,
        StartMode = reader.IsDBNull(8) ? null : reader.GetString(8),
        State = reader.IsDBNull(9) ? null : reader.GetString(9),
        Owner = reader.IsDBNull(10) ? null : reader.GetString(10),
        Location = reader.IsDBNull(11) ? null : reader.GetString(11),
        SizeBytes = reader.IsDBNull(12) ? null : reader.GetInt64(12),
        ModifiedUtc = reader.IsDBNull(13) ? null : DbTime.FromDb(reader.GetString(13)),
        IsMicrosoft = reader.GetInt32(14) == 1,
        IsSecuritySensitive = reader.GetInt32(15) == 1,
        SupportsPause = reader.GetInt32(16) == 1,
        Pause = StorageJson.Deserialize<PauseDescriptor>(reader.IsDBNull(17) ? null : reader.GetString(17)),
        Details = StorageJson.Deserialize<Dictionary<string, string>>(reader.IsDBNull(18) ? null : reader.GetString(18)) ?? new Dictionary<string, string>()
    };

    private sealed class EventAggregate
    {
        public DateTimeOffset? LastEvent { get; init; }
        public DateTimeOffset? LastUpdate { get; init; }
        public DateTimeOffset? InstalledAt { get; init; }
        public DateTimeOffset? LastChange { get; init; }
        public int Attention { get; init; }
        public int Unread { get; init; }
        public int Total { get; init; }
        public int Recent { get; init; }
    }
}
