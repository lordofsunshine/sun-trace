using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.IntegrationTests;

public sealed class HistoryStoreTests
{
    [Fact]
    public async Task SaveSnapshotPersistsApplicationsComponentsAndStatistics()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var snapshot = SnapshotFixtures.Snapshot(DateTimeOffset.Now, SnapshotFixtures.App());
        snapshot.Components.Add(SnapshotFixtures.Startup());

        var snapshotId = await environment.Store.SaveSnapshotAsync(snapshot);

        Assert.True(snapshotId > 0);
        var latest = await environment.Store.GetLatestSnapshotAsync();
        Assert.NotNull(latest);
        Assert.Equal(snapshotId, latest!.Id);
        Assert.Single(latest.Apps);
        Assert.Single(latest.Components);

        var summary = Assert.Single(await environment.Store.GetAppsAsync());
        Assert.Equal("Acme Tool", summary.Name);
        Assert.Equal(1, summary.ComponentCount);
        Assert.Equal("1 компонент", summary.ComponentsLabel);

        var components = await environment.Store.GetAppComponentsAsync(summary.Id);
        Assert.Single(components);
        Assert.Equal("startup-acme", components[0].Id);

        var search = await environment.Store.SearchAsync("Acme");
        Assert.Contains(search, result => result.Kind == SearchResultKind.Application && result.TargetId == summary.Id);
        Assert.Contains(search, result => result.Kind == SearchResultKind.Component);

        var stats = await environment.Store.GetDashboardStatsAsync();
        Assert.Equal(1, stats.AppCount);
        Assert.Equal(1, stats.SnapshotCount);
        Assert.Equal(1, stats.StartupCount);
        Assert.True(stats.LastScanComplete);
    }

    [Fact]
    public async Task SnapshotPayloadRoundTripPreservesComponentDetails()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var snapshot = SnapshotFixtures.Snapshot(DateTimeOffset.Now, SnapshotFixtures.App());
        var component = SnapshotFixtures.Startup();
        component.Details["Дополнительно"] = "Проверено";
        component.Pause!.OriginalSignature = "signature";
        snapshot.Components.Add(component);

        var id = await environment.Store.SaveSnapshotAsync(snapshot);
        var restored = await environment.Store.GetSnapshotAsync(id);

        Assert.NotNull(restored);
        var restoredComponent = Assert.Single(restored!.Components);
        Assert.Equal("Проверено", restoredComponent.Details["Дополнительно"]);
        Assert.Equal("signature", restoredComponent.Pause?.OriginalSignature);
        Assert.Equal(PauseTargetKind.RegistryStartup, restoredComponent.Pause?.Kind);
    }

    [Fact]
    public async Task EventsAreDeduplicatedWithinWindowAndQueryable()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var item = new ChangeEvent
        {
            Fingerprint = "fp-dedupe",
            AppId = "app-acme",
            AppName = "Acme Tool",
            Kind = ChangeKind.Added,
            ComponentType = ComponentType.Application,
            ComponentName = "Acme Tool",
            Title = "Программа установлена",
            Description = "«Acme Tool» появилась на компьютере.",
            Confidence = ConfidenceLevel.High,
            Importance = ImportanceLevel.Normal,
            Source = "Список установленных программ",
            DetectedAt = DateTimeOffset.Now
        };

        Assert.Equal(1, await environment.Store.AppendEventsAsync([item], TimeSpan.FromHours(1)));
        Assert.Equal(0, await environment.Store.AppendEventsAsync([item], TimeSpan.FromHours(1)));

        var page = await environment.Store.QueryEventsAsync(new EventQuery { SearchTerm = "Acme" });
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Программа установлена", page.Items[0].Title);
        Assert.Equal("app-acme", page.Items[0].AppId);

        Assert.Equal(1, await environment.Store.AppendEventsAsync([item], TimeSpan.Zero));
        Assert.Equal(2, (await environment.Store.QueryEventsAsync(new EventQuery())).TotalCount);
    }

    [Fact]
    public async Task PruneSnapshotsKeepsConfiguredCount()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        for (var index = 0; index < 6; index++)
        {
            var app = SnapshotFixtures.App();
            app.Version = $"1.0.{index}";
            var snapshot = SnapshotFixtures.Snapshot(DateTimeOffset.Now.AddMinutes(index), app);
            await environment.Store.SaveSnapshotAsync(snapshot);
        }

        await environment.Store.PruneSnapshotsAsync(5);

        var snapshots = await environment.Store.GetSnapshotsAsync(100);
        Assert.Equal(5, snapshots.Count);
    }

    [Fact]
    public async Task UnreadEventsDriveStatusUntilTheyAreMarkedRead()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await environment.Store.SaveSnapshotAsync(SnapshotFixtures.Snapshot(DateTimeOffset.Now, SnapshotFixtures.App()));
        await environment.Store.AppendEventsAsync([
            new ChangeEvent
            {
                Fingerprint = "fp-unread",
                AppId = "app-acme",
                AppName = "Acme Tool",
                Kind = ChangeKind.Modified,
                ComponentType = ComponentType.Application,
                ComponentName = "Acme Tool",
                Title = "Acme Tool updated",
                Description = "Version 1.1.0",
                Confidence = ConfidenceLevel.High,
                Importance = ImportanceLevel.Normal,
                Source = "Program list",
                DetectedAt = DateTimeOffset.Now
            }
        ], TimeSpan.FromHours(1));

        var unread = Assert.Single(await environment.Store.GetAppsAsync());
        Assert.Equal(1, unread.UnreadCount);
        Assert.Equal(AppStatus.Changed, unread.Status);

        await environment.Store.MarkAppEventsReadAsync("app-acme");

        var read = Assert.Single(await environment.Store.GetAppsAsync());
        Assert.Equal(0, read.UnreadCount);
        Assert.Equal(AppStatus.Unchanged, read.Status);
    }

    [Fact]
    public async Task ClearHistoryRemovesSnapshotsEventsAndApps()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await environment.Store.SaveSnapshotAsync(SnapshotFixtures.Snapshot(DateTimeOffset.Now, SnapshotFixtures.App()));
        await environment.Store.AppendEventsAsync([
            new ChangeEvent
            {
                Fingerprint = "fp-clear",
                AppId = "app-acme",
                AppName = "Acme Tool",
                Kind = ChangeKind.Added,
                ComponentType = ComponentType.Application,
                ComponentName = "Acme Tool",
                Title = "Программа установлена",
                Description = "Тестовое событие",
                DetectedAt = DateTimeOffset.Now
            }
        ], TimeSpan.Zero);

        await environment.Store.ClearHistoryAsync();

        var stats = await environment.Store.GetDashboardStatsAsync();
        Assert.Equal(0, stats.AppCount);
        Assert.Equal(0, stats.EventCount);
        Assert.Equal(0, stats.SnapshotCount);
        Assert.Null(await environment.Store.GetLatestSnapshotAsync());
    }

    [Fact]
    public async Task IntegrityCheckReportsHealthyDatabase()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var health = await environment.Store.CheckIntegrityAsync();

        Assert.True(health.IsHealthy);
        Assert.Equal(1, health.SchemaVersion);
        Assert.True(health.SizeBytes > 0);
    }

    [Fact]
    public async Task LegacySchemaIsArchivedAndRecreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "SunTraceTests", Guid.NewGuid().ToString("N"));
        var paths = new SunTrace.Infrastructure.DataPaths(root);
        Directory.CreateDirectory(root);
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
        };
        await using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString()))
        {
            await legacy.OpenAsync();
            await using var command = legacy.CreateCommand();
            command.CommandText = "CREATE TABLE Apps (Id TEXT PRIMARY KEY, Name TEXT); PRAGMA user_version=1;";
            await command.ExecuteNonQueryAsync();
        }

        var store = new SunTrace.Infrastructure.Storage.SqliteHistoryStore(
            paths,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SunTrace.Infrastructure.Storage.SqliteHistoryStore>.Instance);
        try
        {
            await store.InitializeAsync();
            var stats = await store.GetDashboardStatsAsync();
            Assert.Equal(0, stats.SnapshotCount);
            Assert.Single(Directory.GetFiles(root, "database.legacy-*.db"));
        }
        finally
        {
            await store.DisposeAsync();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }

                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }
        }
    }

    [Fact]
    public async Task CancelledInitializationThrowsOperationCanceled()
    {
        var root = Path.Combine(Path.GetTempPath(), "SunTraceTests", Guid.NewGuid().ToString("N"));
        var paths = new SunTrace.Infrastructure.DataPaths(root);
        var store = new SunTrace.Infrastructure.Storage.SqliteHistoryStore(
            paths,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SunTrace.Infrastructure.Storage.SqliteHistoryStore>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InitializeAsync(cancellation.Token));
        }
        finally
        {
            await store.DisposeAsync();
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
