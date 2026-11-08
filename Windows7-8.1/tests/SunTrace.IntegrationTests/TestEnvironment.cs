using Microsoft.Extensions.Logging.Abstractions;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure;
using SunTrace.Infrastructure.Storage;

namespace SunTrace.IntegrationTests;

internal sealed class TestEnvironment : IAsyncDisposable
{
    private TestEnvironment(DataPaths paths, SqliteHistoryStore store)
    {
        Paths = paths;
        Store = store;
    }

    public DataPaths Paths { get; }

    public SqliteHistoryStore Store { get; }

    public static async Task<TestEnvironment> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "SunTraceTests", Guid.NewGuid().ToString("N"));
        var paths = new DataPaths(root);
        var store = new SqliteHistoryStore(paths, NullLogger<SqliteHistoryStore>.Instance);
        await store.InitializeAsync();
        return new TestEnvironment(paths, store);
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(Paths.Root))
                {
                    return;
                }

                Directory.Delete(Paths.Root, true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(100);
            }
        }
    }
}

internal static class SnapshotFixtures
{
    public static SystemSnapshot Snapshot(DateTimeOffset capturedAt, params InstalledApp[] apps)
    {
        var snapshot = new SystemSnapshot
        {
            CapturedAt = capturedAt,
            Reason = ScanReason.Manual
        };
        snapshot.Apps.AddRange(apps);
        foreach (var section in Enum.GetValues<ScanSection>())
        {
            snapshot.Sections[section] = new SectionReport
            {
                Section = section,
                Status = SectionStatus.Complete,
                ItemsScanned = 1
            };
        }

        return snapshot;
    }

    public static InstalledApp App(string id = "app-acme", string name = "Acme Tool") => new()
    {
        Id = id,
        Name = name,
        Version = "1.0.0",
        Publisher = "Acme, Inc.",
        InstallLocation = @"C:\Program Files\Acme",
        InstallDate = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
        UninstallString = @"""C:\Program Files\Acme\uninstall.exe""",
        MainExecutablePath = @"C:\Program Files\Acme\AcmeTool.exe",
        RegistryKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Acme",
        Source = "Реестр"
    };

    public static ComponentSnapshot Startup(string id = "startup-acme", string name = "Acme Agent") => new()
    {
        Id = id,
        Type = ComponentType.Startup,
        Name = name,
        Detail = @"C:\Program Files\Acme\agent.exe",
        ExecutablePath = @"C:\Program Files\Acme\agent.exe",
        Publisher = "Acme LLC",
        Enabled = true,
        SupportsPause = true,
        Owner = "HKCU\\Run",
        Location = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
        Details = new Dictionary<string, string> { ["Триггер"] = "При входе" },
        Pause = new PauseDescriptor
        {
            Kind = PauseTargetKind.RegistryStartup,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            ApproveRegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            ValueName = "AcmeAgent"
        }
    };
}
