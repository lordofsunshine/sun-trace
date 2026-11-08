using Microsoft.Extensions.Logging.Abstractions;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Services;
using SunTrace.Infrastructure.Settings;

namespace SunTrace.IntegrationTests;

public sealed class BackupSettingsPauseTests
{
    [Fact]
    public async Task BackupCanBeValidatedAndRestored()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await environment.Store.SaveSnapshotAsync(SnapshotFixtures.Snapshot(DateTimeOffset.Now, SnapshotFixtures.App()));
        var service = new BackupService(environment.Paths, NullLogger<BackupService>.Instance);

        var path = await service.CreateBackupAsync();
        var validation = await service.ValidateAsync(path);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".json"));
        Assert.True(validation.IsValid);
        Assert.Equal(1, validation.SchemaVersion);
        Assert.Equal(1, validation.SnapshotCount);

        await environment.Store.SaveSnapshotAsync(SnapshotFixtures.Snapshot(
            DateTimeOffset.Now.AddMinutes(1),
            SnapshotFixtures.App("app-other", "Other Tool")));
        var beforeRestore = await environment.Store.GetAppsAsync();
        Assert.Single(beforeRestore);
        Assert.Equal("Other Tool", beforeRestore[0].Name);

        await service.RestoreAsync(path, environment.Store);

        var restored = Assert.Single(await environment.Store.GetAppsAsync());
        Assert.Equal("Acme Tool", restored.Name);
        var backups = await service.ListBackupsAsync();
        Assert.Contains(backups, backup => backup.FileName.Contains("before-restore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidBackupIsRejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new BackupService(environment.Paths, NullLogger<BackupService>.Instance);
        var path = Path.Combine(environment.Paths.Root, "broken.db");
        await File.WriteAllTextAsync(path, "not a database");

        var validation = await service.ValidateAsync(path);

        Assert.False(validation.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(validation.Message));
    }

    [Fact]
    public async Task SettingsRoundTripAndRaiseChangeEvent()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        var changes = 0;
        service.SettingsChanged += (_, _) => changes++;

        await service.SaveAsync(new AppSettings
        {
            Theme = ThemeMode.Dark,
            MonitoringEnabled = false,
            ScanIntervalMinutes = 30,
            SnapshotRetention = 12
        });

        var reloaded = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await reloaded.LoadAsync();

        Assert.Equal(1, changes);
        Assert.Equal(ThemeMode.Dark, reloaded.Current.Theme);
        Assert.False(reloaded.Current.MonitoringEnabled);
        Assert.Equal(30, reloaded.Current.ScanIntervalMinutes);
        Assert.Equal(12, reloaded.Current.SnapshotRetention);
        Assert.Contains("\"theme\"", await File.ReadAllTextAsync(environment.Paths.SettingsPath));
    }

    [Fact]
    public async Task CorruptedSettingsFallBackToDefaults()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await File.WriteAllTextAsync(environment.Paths.SettingsPath, "{ broken json");
        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);

        await service.LoadAsync();

        Assert.Equal(ThemeMode.System, service.Current.Theme);
        Assert.True(service.Current.MonitoringEnabled);
        Assert.Equal(15, service.Current.ScanIntervalMinutes);
    }

    [Fact]
    public async Task SupportedComponentCanBePausedAndRestored()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new InMemoryComponentPauseService(environment.Store);
        var component = SnapshotFixtures.Startup();

        var paused = await service.PauseAsync(component, "app-acme", "Acme Tool", "Проверка");
        Assert.True(paused.Success);
        Assert.NotNull(paused.Paused);
        Assert.Equal(PauseStatus.Active, paused.Paused!.Status);

        var active = Assert.Single(await environment.Store.GetPausedAsync(true));
        Assert.Equal(component.Id, active.ComponentId);

        var duplicate = await service.PauseAsync(component, "app-acme", "Acme Tool", "Проверка");
        Assert.False(duplicate.Success);

        var restored = await service.RestoreAsync(paused.Paused);
        Assert.Equal(RestoreOutcome.Restored, restored.Outcome);
        Assert.Empty(await environment.Store.GetPausedAsync(true));

        var events = await environment.Store.QueryEventsAsync(new EventQuery());
        Assert.Equal(2, events.TotalCount);
        Assert.Contains(events.Items, item => item.Kind == ChangeKind.Restored);
    }

    [Fact]
    public async Task RestoreConflictCanKeepCurrentState()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new InMemoryComponentPauseService(environment.Store);
        var paused = await service.PauseAsync(SnapshotFixtures.Startup("startup-conflict", "Conflict Agent"), null, null, null);
        Assert.True(paused.Success);

        var result = await service.KeepCurrentStateAsync(paused.Paused!);

        Assert.Equal(RestoreOutcome.Conflict, result.Outcome);
        Assert.Equal(PauseStatus.KeptCurrent, result.Item?.Status);
        Assert.Empty(await environment.Store.GetPausedAsync(true));
    }

    [Fact]
    public async Task ComponentWithoutPauseDescriptorIsRejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new InMemoryComponentPauseService(environment.Store);
        var component = SnapshotFixtures.Startup();
        component.Pause = null;

        var result = await service.PauseAsync(component, null, null, null);

        Assert.False(result.Success);
        Assert.Empty(await environment.Store.GetPausedAsync(true));
    }

    [Fact]
    public async Task PausePersistsDescriptorForLaterRestore()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var service = new InMemoryComponentPauseService(environment.Store);
        var component = SnapshotFixtures.Startup();
        var paused = await service.PauseAsync(component, "app-acme", "Acme Tool", "Проверка");

        var stored = Assert.Single(await environment.Store.GetPausedAsync(true));

        Assert.Equal(paused.Paused!.Descriptor.RegistryPath, stored.Descriptor.RegistryPath);
        Assert.Equal(paused.Paused.Descriptor.ApproveRegistryPath, stored.Descriptor.ApproveRegistryPath);
        Assert.Equal(paused.Paused.Descriptor.ValueName, stored.Descriptor.ValueName);
    }
}
