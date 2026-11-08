using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<long> AddPausedAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO paused_components (component_id, component_type, app_id, app_name, display_name, detail, descriptor_blob,
                                           paused_at, reason, status, last_error)
            VALUES ($componentId, $type, $appId, $appName, $displayName, $detail, $descriptor, $pausedAt, $reason, $status, $error)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$componentId", item.ComponentId);
        command.Parameters.AddWithValue("$type", (int)item.ComponentType);
        command.Parameters.AddWithValue("$appId", (object?)item.AppId ?? DBNull.Value);
        command.Parameters.AddWithValue("$appName", item.AppName ?? string.Empty);
        command.Parameters.AddWithValue("$displayName", item.DisplayName);
        command.Parameters.AddWithValue("$detail", (object?)item.Detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$descriptor", SecretProtector.Protect(StorageJson.Serialize(item.Descriptor)));
        command.Parameters.AddWithValue("$pausedAt", DbTime.ToDb(item.PausedAt));
        command.Parameters.AddWithValue("$reason", (object?)item.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)item.Status);
        command.Parameters.AddWithValue("$error", (object?)item.LastError ?? DBNull.Value);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        item.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<PausedComponent>> GetPausedAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, component_id, component_type, app_id, app_name, display_name, detail, descriptor_blob, paused_at,
                   reason, status, last_error
            FROM paused_components
            {(activeOnly ? "WHERE status IN (0, 2, 3, 4)" : string.Empty)}
            ORDER BY paused_at DESC;
            """;
        var result = new List<PausedComponent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var descriptorJson = SecretProtector.Unprotect(reader.IsDBNull(7) ? null : reader.GetString(7));
            var descriptor = StorageJson.Deserialize<PauseDescriptor>(descriptorJson);
            if (descriptor is null)
            {
                _logger.LogWarning("Запись Safe Pause {Id} повреждена и пропущена.", reader.GetInt64(0));
                continue;
            }

            result.Add(new PausedComponent
            {
                Id = reader.GetInt64(0),
                ComponentId = reader.GetString(1),
                ComponentType = (ComponentType)reader.GetInt32(2),
                AppId = reader.IsDBNull(3) ? null : reader.GetString(3),
                AppName = reader.GetString(4),
                DisplayName = reader.GetString(5),
                Detail = reader.IsDBNull(6) ? null : reader.GetString(6),
                Descriptor = descriptor,
                PausedAt = DbTime.FromDb(reader.GetString(8)) ?? DateTimeOffset.MinValue,
                Reason = reader.IsDBNull(9) ? null : reader.GetString(9),
                Status = (PauseStatus)reader.GetInt32(10),
                LastError = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }

        return result;
    }

    public async Task UpdatePausedAsync(PausedComponent item, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE paused_components
            SET descriptor_blob = $descriptor, status = $status, last_error = $error
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$descriptor", SecretProtector.Protect(StorageJson.Serialize(item.Descriptor)));
        command.Parameters.AddWithValue("$status", (int)item.Status);
        command.Parameters.AddWithValue("$error", (object?)item.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", item.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemovePausedAsync(long id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM paused_components WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
