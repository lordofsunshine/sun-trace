using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in new[] { "events", "snapshots", "components", "apps", "sessions" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var resolved = connection.CreateCommand())
        {
            resolved.Transaction = transaction;
            resolved.CommandText = "DELETE FROM paused_components WHERE status NOT IN (0, 2, 3, 4);";
            await resolved.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("История SunTrace удалена по запросу пользователя.");
    }

    public async Task<DatabaseHealth> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var message = "ok";
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            message = (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "нет ответа";
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }

        var version = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        var size = File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
        var healthy = string.Equals(message, "ok", StringComparison.OrdinalIgnoreCase);

        return new DatabaseHealth
        {
            IsHealthy = healthy,
            Message = healthy ? "База данных в порядке." : "Проверка целостности: " + message,
            SizeBytes = size,
            SchemaVersion = version
        };
    }

    private async Task ArchiveLegacySchemaAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DatabasePath))
        {
            return;
        }

        var legacy = false;
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'apps' COLLATE NOCASE;";
            var hasApps = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
            if (!hasApps)
            {
                return;
            }

            command.CommandText = "PRAGMA table_info(apps);";
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                columns.Add(reader.GetString(1));
            }

            legacy = !columns.Contains("present");
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Не удалось проверить схему существующей базы данных.");
            return;
        }

        if (!legacy)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        var archive = Path.Combine(Path.GetDirectoryName(DatabasePath)!, $"database.legacy-{suffix}.db");
        File.Move(DatabasePath, archive);
        foreach (var extra in new[] { "-wal", "-shm" })
        {
            var source = DatabasePath + extra;
            if (File.Exists(source))
            {
                File.Move(source, archive + extra, true);
            }
        }

        _logger.LogWarning("Обнаружена несовместимая старая база. Файл сохранён как {Archive}; создана новая база.", archive);
    }
}
