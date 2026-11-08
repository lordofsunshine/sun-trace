using System.Globalization;
using Microsoft.Data.Sqlite;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var week = DbTime.ToDb(DateTimeOffset.Now.AddDays(-7));
        var month = DbTime.ToDb(DateTimeOffset.Now.AddDays(-30));

        var appCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM apps WHERE present = 1;", cancellationToken).ConfigureAwait(false);
        var eventCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM events;", cancellationToken).ConfigureAwait(false);
        var eventsWeek = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM events WHERE detected_at >= $p;", cancellationToken, week).ConfigureAwait(false);
        var updatedApps = await ScalarIntAsync(connection,
            "SELECT COUNT(DISTINCT app_id) FROM events WHERE component_type = 0 AND kind = 2 AND detected_at >= $p;",
            cancellationToken, month).ConfigureAwait(false);
        var attention = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM events WHERE importance = 2 AND is_reverted = 0 AND is_read = 0;",
            cancellationToken).ConfigureAwait(false);
        var unread = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM events WHERE is_reverted = 0 AND is_read = 0;",
            cancellationToken).ConfigureAwait(false);
        var paused = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM paused_components WHERE status = 0;", cancellationToken).ConfigureAwait(false);
        var changedApps = await ScalarIntAsync(connection,
            "SELECT COUNT(DISTINCT app_id) FROM events WHERE component_type NOT IN (5, 6) AND detected_at >= $p;",
            cancellationToken, week).ConfigureAwait(false);
        var snapshots = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM snapshots;", cancellationToken).ConfigureAwait(false);
        var startups = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM components WHERE status = 'active' AND type = 1;", cancellationToken).ConfigureAwait(false);
        var services = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM components WHERE status = 'active' AND type = 2;", cancellationToken).ConfigureAwait(false);
        var tasks = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM components WHERE status = 'active' AND type = 3;", cancellationToken).ConfigureAwait(false);

        DateTimeOffset? lastScan = null;
        ScanReason? reason = null;
        var complete = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT captured_at, reason, is_complete FROM snapshots ORDER BY id DESC LIMIT 1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lastScan = DbTime.FromDb(reader.GetString(0));
                reason = (ScanReason)reader.GetInt32(1);
                complete = reader.GetInt32(2) == 1;
            }
        }

        return new DashboardStats
        {
            AppCount = appCount,
            EventCount = eventCount,
            EventsLast7Days = eventsWeek,
            UpdatedAppsLast30Days = updatedApps,
            AttentionCount = attention,
            UnreadCount = unread,
            PausedCount = paused,
            ChangedAppCount = changedApps,
            LastScanAt = lastScan,
            LastScanReason = reason,
            LastScanComplete = complete,
            SnapshotCount = snapshots,
            StartupCount = startups,
            ServiceCount = services,
            TaskCount = tasks
        };
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string term, int limit = 24, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return Array.Empty<SearchResult>();
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var pattern = "%" + EscapeLike(term.Trim()) + "%";
        var results = new List<SearchResult>();

        await using (var apps = connection.CreateCommand())
        {
            apps.CommandText = """
                SELECT id, name, publisher, version, present FROM apps
                WHERE name LIKE $term ESCAPE '\' OR publisher LIKE $term ESCAPE '\'
                ORDER BY name COLLATE NOCASE LIMIT $limit;
                """;
            apps.Parameters.AddWithValue("$term", pattern);
            apps.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
            await using var reader = await apps.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var present = reader.GetInt32(4) == 1;
                var publisher = reader.IsDBNull(2) ? "Издатель не указан" : reader.GetString(2);
                var version = reader.IsDBNull(3) ? "версия не указана" : reader.GetString(3);
                results.Add(new SearchResult
                {
                    Kind = SearchResultKind.Application,
                    Title = reader.GetString(1),
                    Subtitle = present ? $"{publisher} · {version}" : "Программа удалена · сохранена история",
                    TargetId = reader.GetString(0),
                    AppId = reader.GetString(0)
                });
            }
        }

        await using (var events = connection.CreateCommand())
        {
            events.CommandText = """
                SELECT id, title, app_name, detected_at FROM events
                WHERE title LIKE $term ESCAPE '\' OR description LIKE $term ESCAPE '\' OR app_name LIKE $term ESCAPE '\' OR component_name LIKE $term ESCAPE '\'
                ORDER BY detected_at DESC LIMIT $limit;
                """;
            events.Parameters.AddWithValue("$term", pattern);
            events.Parameters.AddWithValue("$limit", Math.Clamp(limit / 2, 1, 30));
            await using var reader = await events.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var appName = reader.IsDBNull(2) ? "Связь не определена" : reader.GetString(2);
                var when = DbTime.FromDb(reader.GetString(3))?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
                results.Add(new SearchResult
                {
                    Kind = SearchResultKind.Event,
                    Title = reader.GetString(1),
                    Subtitle = $"{appName} · {when}",
                    TargetId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        await using (var components = connection.CreateCommand())
        {
            components.CommandText = """
                SELECT id, name, type, detail FROM components
                WHERE status = 'active' AND (name LIKE $term ESCAPE '\' OR detail LIKE $term ESCAPE '\')
                ORDER BY name COLLATE NOCASE LIMIT $limit;
                """;
            components.Parameters.AddWithValue("$term", pattern);
            components.Parameters.AddWithValue("$limit", Math.Clamp(limit / 2, 1, 20));
            await using var reader = await components.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = (ComponentType)reader.GetInt32(2);
                results.Add(new SearchResult
                {
                    Kind = SearchResultKind.Component,
                    Title = reader.GetString(1),
                    Subtitle = type switch
                    {
                        ComponentType.Startup => "Элемент автозагрузки",
                        ComponentType.Service => "Служба Windows",
                        ComponentType.ScheduledTask => "Задание планировщика",
                        ComponentType.File => "Файл программы",
                        ComponentType.Folder => "Папка программы",
                        _ => "Компонент"
                    } + (reader.IsDBNull(3) ? string.Empty : " · " + Truncate(reader.GetString(3), 70)),
                    TargetId = reader.GetString(0)
                });
            }
        }

        return results.Take(limit).ToList();
    }

    public async Task<IReadOnlyList<string>> GetKnownAppNamesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT app_name FROM events WHERE app_name <> '' ORDER BY app_name COLLATE NOCASE;";
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}
