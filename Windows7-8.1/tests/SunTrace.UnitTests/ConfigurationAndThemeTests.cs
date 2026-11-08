using System.Xml.Linq;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure;
using SunTrace.Infrastructure.Recovery;

namespace SunTrace.UnitTests;

public sealed class ConfigurationAndThemeTests
{
    [Fact]
    public void SettingsNormalizeInvalidValues()
    {
        var settings = new AppSettings
        {
            Theme = ThemeMode.Dark,
            ScanIntervalMinutes = 7,
            SnapshotRetention = 2,
            WindowWidth = 700,
            WindowHeight = 500
        };

        var normalized = settings.Normalize();

        Assert.Equal(15, normalized.ScanIntervalMinutes);
        Assert.Equal(40, normalized.SnapshotRetention);
        Assert.Equal(1280, normalized.WindowWidth);
        Assert.Equal(820, normalized.WindowHeight);
        Assert.Equal(ThemeMode.Dark, normalized.Theme);
    }

    [Fact]
    public void CloneDoesNotShareMutableState()
    {
        var original = new AppSettings { Theme = ThemeMode.Dark, MonitoringEnabled = true };
        var clone = original.Clone();

        clone.Theme = ThemeMode.Light;
        clone.MonitoringEnabled = false;

        Assert.Equal(ThemeMode.Dark, original.Theme);
        Assert.True(original.MonitoringEnabled);
    }

    [Fact]
    public void PathsKeepAllStateInsideTheProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "SunTracePathsTest");
        var paths = new DataPaths(root);

        Assert.Equal(Path.Combine(root, "database.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(root, "settings.json"), paths.SettingsPath);
        Assert.Equal(Path.Combine(root, "backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(root, "exports"), paths.ExportsDirectory);
        Assert.Equal(Path.Combine(root, "iconcache"), paths.IconCacheDirectory);
    }

    [Fact]
    public void StartupApprovedValuesMatchWindowsEncoding()
    {
        var disabled = PauseController.CreateDisabledValue();
        var enabled = PauseController.CreateEnabledValue();

        Assert.Equal(3, disabled[0]);
        Assert.Equal(2, enabled[0]);
        Assert.True(PauseController.IsDisabled(disabled));
        Assert.False(PauseController.IsDisabled(enabled));
        Assert.True(PauseController.IsDisabled([7]));
        Assert.False(PauseController.IsDisabled(null));
    }

    [Fact]
    public void ElevationRequiredForMachineAndTaskTargets()
    {
        Assert.True(PauseController.RequiresElevation(new PauseDescriptor { Kind = PauseTargetKind.ScheduledTask }));
        Assert.True(PauseController.RequiresElevation(new PauseDescriptor
        {
            Kind = PauseTargetKind.RegistryStartup,
            RegistryPath = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run"
        }));
        Assert.False(PauseController.RequiresElevation(new PauseDescriptor
        {
            Kind = PauseTargetKind.RegistryStartup,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
        }));
    }

    [Fact]
    public void MissingRegistryTargetReturnsMessageInsteadOfThrowing()
    {
        var result = PauseController.Pause(new PauseDescriptor
        {
            Kind = PauseTargetKind.RegistryStartup,
            RegistryPath = @"HKCU\Software\SunTraceTests\Missing",
            ApproveRegistryPath = @"HKCU\Software\SunTraceTests\MissingApproved",
            ValueName = "MissingValue"
        });

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void SignaturesDependOnState()
    {
        var enabled = PauseController.Signature("agent.exe", true, "logon");
        var disabled = PauseController.Signature("agent.exe", false, "logon");

        Assert.NotEqual(enabled, disabled);
        Assert.Equal(64, enabled.Length);
    }

    [Fact]
    public void LightAndDarkPalettesExposeSameResources()
    {
        var directory = FindThemesDirectory();
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var light = ReadResources(Path.Combine(directory, "Palette.Light.xaml"), xaml);
        var dark = ReadResources(Path.Combine(directory, "Palette.Dark.xaml"), xaml);

        Assert.Equal(light.Keys.OrderBy(key => key), dark.Keys.OrderBy(key => key));
        Assert.Contains("App.Accent", light.Keys);
        Assert.Contains("App.Background", light.Keys);
        Assert.Contains("App.Surface", light.Keys);
        Assert.NotEqual(light["App.Background"], dark["App.Background"]);
        Assert.NotEqual(light["App.Text"], dark["App.Text"]);
    }

    private static Dictionary<string, string> ReadResources(string path, XNamespace xaml)
    {
        var document = XDocument.Load(path);
        return document.Descendants()
            .Select(element => new
            {
                Key = (string?)element.Attribute(xaml + "Key"),
                Value = (string?)element.Attribute("Color") ?? element.Value.Trim()
            })
            .Where(item => item.Key is not null)
            .ToDictionary(item => item.Key!, item => item.Value);
    }

    private static string FindThemesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "SunTrace.App", "Themes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найдена папка тем SunTrace.");
    }
}
