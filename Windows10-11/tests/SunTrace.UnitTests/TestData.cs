using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 30, 0, TimeSpan.Zero);

    public static SystemSnapshot Snapshot(params InstalledApp[] apps)
    {
        var snapshot = new SystemSnapshot
        {
            CapturedAt = Now,
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

    public static InstalledApp App(
        string id = "app-acme",
        string name = "Acme Tool",
        string version = "1.0.0",
        string publisher = "Acme, Inc.",
        string location = @"C:\Program Files\Acme") => new()
    {
        Id = id,
        Name = name,
        Version = version,
        Publisher = publisher,
        InstallLocation = location,
        InstallDate = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
        UninstallString = @"""C:\Program Files\Acme\uninstall.exe""",
        MainExecutablePath = @"C:\Program Files\Acme\AcmeTool.exe",
        RegistryKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Acme",
        Source = "Реестр"
    };

    public static ComponentSnapshot Startup(
        string id = "startup-acme",
        string name = "Acme Agent",
        string executable = @"C:\Program Files\Acme\agent.exe") => new()
    {
        Id = id,
        Type = ComponentType.Startup,
        Name = name,
        Detail = executable,
        ExecutablePath = executable,
        Publisher = "Acme LLC",
        Enabled = true,
        SupportsPause = true,
        Owner = "HKCU\\Run",
        Location = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
        Pause = new PauseDescriptor
        {
            Kind = PauseTargetKind.RegistryStartup,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            ApproveRegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            ValueName = "AcmeAgent"
        }
    };
}
