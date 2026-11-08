using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Storage;

namespace SunTrace.Infrastructure.Services;

public sealed class BackupService : IBackupService
{
    private static readonly string[] RequiredTables = ["apps", "components", "events", "sessions", "snapshots"];
    private static readonly string[] RestoredTables = ["events", "snapshots", "components", "apps", "sessions"];

    private readonly DataPaths _paths;
    private readonly ILogger<BackupService> _logger;

    public BackupService(DataPaths paths, ILogger<BackupService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException("База данных ещё не создана.", _paths.DatabasePath);
        }

        var path = UniquePath(_paths.BackupsDirectory, "SunTrace-backup");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"VACUUM INTO '{path.Replace("'", "''")}';";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var validation = await ValidateAsync(path, cancellationToken).ConfigureAwait(false);
        var metaPath = path + ".json";
        await File.WriteAllTextAsync(metaPath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                created = DateTimeOffset.Now,
                application = "SunTrace",
                version = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                schema = validation.SchemaVersion,
                events = validation.EventCount,
                snapshots = validation.SnapshotCount,
                sizeBytes = validation.SizeBytes
            }),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Создана резервная копия: {Path}.", path);
        return path;
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.BackupsDirectory))
        {
            return Array.Empty<BackupInfo>();
        }

        var files = Directory.EnumerateFiles(_paths.BackupsDirectory, "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new BackupInfo
            {
                Path = info.FullName,
                CreatedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
                SizeBytes = info.Length
            })
            .ToList();

        return await Task.FromResult(files).ConfigureAwait(false);
    }

    public async Task<BackupValidation> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new BackupValidation { IsValid = false, Message = "Файл резервной копии не найден." };
        }

        try
        {
            var size = new FileInfo(path).Length;
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                var result = (await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupValidation { IsValid = false, Message = "Резервная копия повреждена: " + result, SizeBytes = size };
                }
            }

            var version = 0;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            if (version is < 1 or > SqliteSchema.CurrentVersion)
            {
                return new BackupValidation
                {
                    IsValid = false,
                    Message = $"Версия схемы резервной копии ({version}) несовместима с текущей версией SunTrace.",
                    SchemaVersion = version,
                    SizeBytes = size
                };
            }

            foreach (var table in RequiredTables)
            {
                await using var check = connection.CreateCommand();
                check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
                check.Parameters.AddWithValue("$name", table);
                var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (exists == 0)
                {
                    return new BackupValidation
                    {
                        IsValid = false,
                        Message = $"В резервной копии отсутствует таблица «{table}».",
                        SchemaVersion = version,
                        SizeBytes = size
                    };
                }
            }

            var events = await CountAsync(connection, "events", cancellationToken).ConfigureAwait(false);
            var snapshots = await CountAsync(connection, "snapshots", cancellationToken).ConfigureAwait(false);

            return new BackupValidation
            {
                IsValid = true,
                Message = "Резервная копия проверена и совместима.",
                SchemaVersion = version,
                EventCount = events,
                SnapshotCount = snapshots,
                SizeBytes = size
            };
        }
        catch (Exception ex)
        {
            return new BackupValidation { IsValid = false, Message = "Не удалось прочитать резервную копию: " + ex.Message };
        }
    }

    public async Task RestoreAsync(string path, IHistoryStore store, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(path, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        Directory.CreateDirectory(_paths.BackupsDirectory);
        var safetyCopy = UniquePath(_paths.BackupsDirectory, "SunTrace-before-restore");
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                     {
                         DataSource = _paths.DatabasePath,
                         Mode = SqliteOpenMode.ReadWrite
                     }.ToString()))
        {
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var vacuum = source.CreateCommand();
            vacuum.CommandText = $"VACUUM INTO '{safetyCopy.Replace("'", "''")}';";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var attach = connection.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE '{path.Replace("'", "''")}' AS backup;";
            await attach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var table in RestoredTables)
                {
                    var columns = await GetCommonColumnsAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false);
                    if (columns.Length == 0)
                    {
                        throw new InvalidOperationException($"В резервной копии нет подходящих столбцов таблицы «{table}».");
                    }

                    await using var copy = connection.CreateCommand();
                    copy.Transaction = transaction;
                    copy.CommandText = $"DELETE FROM main.{table}; INSERT INTO main.{table} ({columns}) SELECT {columns} FROM backup.{table};";
                    await copy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE backup;";
            await detach.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var health = await store.CheckIntegrityAsync(cancellationToken).ConfigureAwait(false);
        if (!health.IsHealthy)
        {
            throw new InvalidOperationException("После восстановления проверка базы не прошла: " + health.Message);
        }

        _logger.LogInformation("История восстановлена из {Path}. Предыдущая база сохранена как {Safety}.", path, safetyCopy);
    }

    private static string UniquePath(string directory, string prefix)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"{prefix}-{stamp}.db");
        var index = 1;
        while (File.Exists(path) || File.Exists(path + ".json"))
        {
            path = Path.Combine(directory, $"{prefix}-{stamp}-{index}.db");
            index++;
        }

        return path;
    }

    private static async Task<string> GetCommonColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken cancellationToken)
    {
        var main = await ReadColumnsAsync(connection, transaction, "main", table, cancellationToken).ConfigureAwait(false);
        var backup = new HashSet<string>(
            await ReadColumnsAsync(connection, transaction, "backup", table, cancellationToken).ConfigureAwait(false),
            StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", main.Where(backup.Contains).Select(name => "\"" + name.Replace("\"", "\"\"") + "\""));
    }

    private static async Task<List<string>> ReadColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, string schema, string table, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA {schema}.table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(1));
        }

        return result;
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
}
