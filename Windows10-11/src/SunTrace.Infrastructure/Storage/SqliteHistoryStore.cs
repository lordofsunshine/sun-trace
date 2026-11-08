using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Interfaces;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore : IHistoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteHistoryStore> _logger;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public SqliteHistoryStore(DataPaths paths, ILogger<SqliteHistoryStore> logger)
    {
        DatabasePath = paths.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _logger = logger;
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await ArchiveLegacySchemaAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

            var version = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > SqliteSchema.CurrentVersion)
            {
                throw new InvalidOperationException(
                    "База данных создана более новой версией SunTrace. Обновите программу или восстановите резервную копию.");
            }

            if (version == 0)
            {
                await ExecuteAsync(connection, SqliteSchema.CreateVersionOne, cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, $"PRAGMA user_version={SqliteSchema.CurrentVersion};", cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _initializeGate.WaitAsync().ConfigureAwait(false);
        _initializeGate.Release();
        _initializeGate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, string? parameter = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("$p", parameter);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static void AddParameter(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
