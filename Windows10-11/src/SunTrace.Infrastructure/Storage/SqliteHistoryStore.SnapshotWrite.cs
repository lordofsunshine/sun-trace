using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Storage;

public sealed partial class SqliteHistoryStore
{
    public async Task<long> SaveSnapshotAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var content = SerializeContent(snapshot);
        var contentHash = Hashing.Sha256(content);
        var latest = await ReadLatestStampAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (latest is { } stamp && stamp.Hash == contentHash)
        {
            await TouchSnapshotAsync(connection, transaction, stamp.Id, snapshot.CapturedAt, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return stamp.Id;
        }

        var snapshotId = await InsertSnapshotAsync(connection, transaction, snapshot, content, contentHash, cancellationToken).ConfigureAwait(false);
        var now = DbTime.Now;
        await UpsertAppsAsync(connection, transaction, snapshot, now, cancellationToken).ConfigureAwait(false);
        await UpsertComponentsAsync(connection, transaction, snapshot, now, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Снимок сохранён: {Id}, программ: {Apps}, компонентов: {Components}.", snapshotId, snapshot.Apps.Count, snapshot.Components.Count);
        return snapshotId;
    }

    private static async Task<long> InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SystemSnapshot snapshot,
        byte[] content,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var payload = Compress(content);
        var sections = StorageJson.Serialize(snapshot.Sections);
        long snapshotId;

        await using (var insertSnapshot = connection.CreateCommand())
        {
            insertSnapshot.Transaction = transaction;
            insertSnapshot.CommandText = """
                INSERT INTO snapshots (captured_at, reason, app_count, component_count, is_complete, sections_json, payload, payload_hash)
                VALUES ($captured, $reason, $apps, $components, $complete, $sections, $payload, $hash)
                RETURNING id;
                """;
            insertSnapshot.Parameters.AddWithValue("$captured", DbTime.ToDb(snapshot.CapturedAt));
            insertSnapshot.Parameters.AddWithValue("$reason", (int)snapshot.Reason);
            insertSnapshot.Parameters.AddWithValue("$apps", snapshot.Apps.Count);
            insertSnapshot.Parameters.AddWithValue("$components", snapshot.Components.Count);
            insertSnapshot.Parameters.AddWithValue("$complete", snapshot.IsComplete ? 1 : 0);
            insertSnapshot.Parameters.AddWithValue("$sections", sections);
            insertSnapshot.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
            insertSnapshot.Parameters.AddWithValue("$hash", contentHash);
            snapshotId = Convert.ToInt64(await insertSnapshot.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        return snapshotId;
    }

    private static async Task UpsertAppsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SystemSnapshot snapshot,
        string now,
        CancellationToken cancellationToken)
    {
        var applicationsComplete = snapshot.GetSection(ScanSection.Applications).Status == SectionStatus.Complete;

        if (applicationsComplete)
        {
            await using var markRemoved = connection.CreateCommand();
            markRemoved.Transaction = transaction;
            markRemoved.CommandText = "UPDATE apps SET present = 0;";
            await markRemoved.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var upsertApp = connection.CreateCommand())
        {
            upsertApp.Transaction = transaction;
            upsertApp.CommandText = """
                INSERT INTO apps (id, name, publisher, version, install_date, last_updated, install_location, uninstall_string,
                                  display_icon, main_executable, registry_key, signature_publisher, estimated_size, is_system, source,
                                  first_seen, last_seen, present)
                VALUES ($id, $name, $publisher, $version, $installDate, $lastUpdated, $location, $uninstall, $icon, $mainExe,
                        $registryKey, $signature, $size, $system, $source, $now, $now, 1)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    publisher = excluded.publisher,
                    version = excluded.version,
                    install_date = excluded.install_date,
                    install_location = excluded.install_location,
                    uninstall_string = excluded.uninstall_string,
                    display_icon = excluded.display_icon,
                    main_executable = excluded.main_executable,
                    registry_key = excluded.registry_key,
                    signature_publisher = excluded.signature_publisher,
                    estimated_size = excluded.estimated_size,
                    is_system = excluded.is_system,
                    source = excluded.source,
                    last_seen = excluded.last_seen,
                    present = 1;
                """;
            AddParameter(upsertApp, "$id", null);
            AddParameter(upsertApp, "$name", null);
            AddParameter(upsertApp, "$publisher", null);
            AddParameter(upsertApp, "$version", null);
            AddParameter(upsertApp, "$installDate", null);
            AddParameter(upsertApp, "$lastUpdated", null);
            AddParameter(upsertApp, "$location", null);
            AddParameter(upsertApp, "$uninstall", null);
            AddParameter(upsertApp, "$icon", null);
            AddParameter(upsertApp, "$mainExe", null);
            AddParameter(upsertApp, "$registryKey", null);
            AddParameter(upsertApp, "$signature", null);
            AddParameter(upsertApp, "$size", null);
            AddParameter(upsertApp, "$system", null);
            AddParameter(upsertApp, "$source", null);
            AddParameter(upsertApp, "$now", null);

            foreach (var app in snapshot.Apps)
            {
                upsertApp.Parameters["$id"].Value = app.Id;
                upsertApp.Parameters["$name"].Value = app.Name;
                upsertApp.Parameters["$publisher"].Value = (object?)app.Publisher ?? DBNull.Value;
                upsertApp.Parameters["$version"].Value = (object?)app.Version ?? DBNull.Value;
                upsertApp.Parameters["$installDate"].Value = app.InstallDate is null ? DBNull.Value : DbTime.ToDb(app.InstallDate.Value);
                upsertApp.Parameters["$lastUpdated"].Value = app.LastUpdated is null ? DBNull.Value : DbTime.ToDb(app.LastUpdated.Value);
                upsertApp.Parameters["$location"].Value = (object?)app.InstallLocation ?? DBNull.Value;
                upsertApp.Parameters["$uninstall"].Value = (object?)app.UninstallString ?? DBNull.Value;
                upsertApp.Parameters["$icon"].Value = (object?)app.DisplayIcon ?? DBNull.Value;
                upsertApp.Parameters["$mainExe"].Value = (object?)app.MainExecutablePath ?? DBNull.Value;
                upsertApp.Parameters["$registryKey"].Value = (object?)app.RegistryKey ?? DBNull.Value;
                upsertApp.Parameters["$signature"].Value = (object?)app.SignaturePublisher ?? DBNull.Value;
                upsertApp.Parameters["$size"].Value = app.EstimatedSizeBytes is null ? DBNull.Value : app.EstimatedSizeBytes.Value;
                upsertApp.Parameters["$system"].Value = app.IsSystemComponent ? 1 : 0;
                upsertApp.Parameters["$source"].Value = (object?)app.Source ?? DBNull.Value;
                upsertApp.Parameters["$now"].Value = now;
                await upsertApp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task UpsertComponentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SystemSnapshot snapshot,
        string now,
        CancellationToken cancellationToken)
    {
        var completeTypes = GetCompleteComponentTypes(snapshot);
        if (completeTypes.Count > 0)
        {
            await using var deleteMissing = connection.CreateCommand();
            deleteMissing.Transaction = transaction;
            deleteMissing.CommandText = $"DELETE FROM components WHERE type IN ({string.Join(',', completeTypes)});";
            await deleteMissing.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var associationIndex = new AppAssociationIndex(snapshot.Apps);
        await using (var upsertComponent = connection.CreateCommand())
        {
            upsertComponent.Transaction = transaction;
            upsertComponent.CommandText = """
                INSERT INTO components (id, app_id, type, name, detail, executable_path, publisher, signature_publisher, enabled,
                                        start_mode, state, owner, location, size_bytes, modified_utc, is_microsoft,
                                        is_security_sensitive, supports_pause, pause_json, details_json, status, first_seen, last_seen)
                VALUES ($id, $appId, $type, $name, $detail, $executable, $publisher, $signature, $enabled,
                        $startMode, $state, $owner, $location, $size, $modified, $microsoft,
                        $security, $supportsPause, $pause, $details, 'active', $now, $now)
                ON CONFLICT(id) DO UPDATE SET
                    app_id = excluded.app_id,
                    type = excluded.type,
                    name = excluded.name,
                    detail = excluded.detail,
                    executable_path = excluded.executable_path,
                    publisher = excluded.publisher,
                    signature_publisher = excluded.signature_publisher,
                    enabled = excluded.enabled,
                    start_mode = excluded.start_mode,
                    state = excluded.state,
                    owner = excluded.owner,
                    location = excluded.location,
                    size_bytes = excluded.size_bytes,
                    modified_utc = excluded.modified_utc,
                    is_microsoft = excluded.is_microsoft,
                    is_security_sensitive = excluded.is_security_sensitive,
                    supports_pause = excluded.supports_pause,
                    pause_json = excluded.pause_json,
                    details_json = excluded.details_json,
                    status = 'active',
                    last_seen = excluded.last_seen;
                """;
            foreach (var parameter in new[]
                     {
                         "$id", "$appId", "$type", "$name", "$detail", "$executable", "$publisher", "$signature", "$enabled",
                         "$startMode", "$state", "$owner", "$location", "$size", "$modified", "$microsoft", "$security",
                         "$supportsPause", "$pause", "$details", "$now"
                     })
            {
                AddParameter(upsertComponent, parameter, null);
            }

            foreach (var component in snapshot.Components)
            {
                upsertComponent.Parameters["$id"].Value = component.Id;
                upsertComponent.Parameters["$appId"].Value = (object?)associationIndex.Resolve(component) ?? DBNull.Value;
                upsertComponent.Parameters["$type"].Value = (int)component.Type;
                upsertComponent.Parameters["$name"].Value = component.Name;
                upsertComponent.Parameters["$detail"].Value = (object?)component.Detail ?? DBNull.Value;
                upsertComponent.Parameters["$executable"].Value = (object?)component.ExecutablePath ?? DBNull.Value;
                upsertComponent.Parameters["$publisher"].Value = (object?)component.Publisher ?? DBNull.Value;
                upsertComponent.Parameters["$signature"].Value = (object?)component.SignaturePublisher ?? DBNull.Value;
                upsertComponent.Parameters["$enabled"].Value = component.Enabled ? 1 : 0;
                upsertComponent.Parameters["$startMode"].Value = (object?)component.StartMode ?? DBNull.Value;
                upsertComponent.Parameters["$state"].Value = (object?)component.State ?? DBNull.Value;
                upsertComponent.Parameters["$owner"].Value = (object?)component.Owner ?? DBNull.Value;
                upsertComponent.Parameters["$location"].Value = (object?)component.Location ?? DBNull.Value;
                upsertComponent.Parameters["$size"].Value = component.SizeBytes is null ? DBNull.Value : component.SizeBytes.Value;
                upsertComponent.Parameters["$modified"].Value = component.ModifiedUtc is null ? DBNull.Value : DbTime.ToDb(component.ModifiedUtc.Value);
                upsertComponent.Parameters["$microsoft"].Value = component.IsMicrosoft ? 1 : 0;
                upsertComponent.Parameters["$security"].Value = component.IsSecuritySensitive ? 1 : 0;
                upsertComponent.Parameters["$supportsPause"].Value = component.SupportsPause ? 1 : 0;
                upsertComponent.Parameters["$pause"].Value = component.Pause is null ? DBNull.Value : StorageJson.Serialize(component.Pause);
                upsertComponent.Parameters["$details"].Value = StorageJson.Serialize(component.Details);
                upsertComponent.Parameters["$now"].Value = now;
                await upsertComponent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static List<int> GetCompleteComponentTypes(SystemSnapshot snapshot)
    {
        var types = new List<int>();
        if (snapshot.GetSection(ScanSection.Startup).Status == SectionStatus.Complete)
        {
            types.Add((int)ComponentType.Startup);
        }

        if (snapshot.GetSection(ScanSection.Services).Status == SectionStatus.Complete)
        {
            types.Add((int)ComponentType.Service);
        }

        if (snapshot.GetSection(ScanSection.ScheduledTasks).Status == SectionStatus.Complete)
        {
            types.Add((int)ComponentType.ScheduledTask);
        }

        if (snapshot.GetSection(ScanSection.Files).Status == SectionStatus.Complete)
        {
            types.Add((int)ComponentType.File);
            types.Add((int)ComponentType.Folder);
        }

        return types;
    }
}
