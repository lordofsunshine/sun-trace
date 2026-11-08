using System.Globalization;
using Microsoft.Win32;
using SunTrace.Core.Detection;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class InventoryScanResult
{
    public List<InstalledApp> Apps { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public bool IsComplete { get; init; }
}

public sealed class SoftwareInventoryScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View, string Label)[] Sources =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM (32-бит)"),
        (RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU")
    ];

    public InventoryScanResult Scan(CancellationToken cancellationToken)
    {
        var apps = new List<InstalledApp>();
        var warnings = new List<string>();
        var complete = true;

        foreach (var source in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(source.Hive, source.View);
                using var uninstall = baseKey.OpenSubKey(UninstallPath, writable: false);
                if (uninstall is null)
                {
                    warnings.Add($"Раздел {source.Label} для списка программ недоступен.");
                    complete = false;
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = uninstall.OpenSubKey(subKeyName, writable: false);
                        if (entry is null)
                        {
                            continue;
                        }

                        var app = ReadEntry(entry, subKeyName, source.Label);
                        if (app is not null)
                        {
                            apps.Add(app);
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        warnings.Add($"Нет доступа к записи {subKeyName} в {source.Label}.");
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Не удалось прочитать список программ {source.Label}: {ex.Message}");
                complete = false;
            }
        }

        var deduplicated = Deduplicate(apps);
        AssignIdentifiers(deduplicated);
        deduplicated.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        return new InventoryScanResult
        {
            Apps = deduplicated,
            Warnings = warnings,
            IsComplete = complete
        };
    }

    private static InstalledApp? ReadEntry(RegistryKey entry, string subKeyName, string hiveLabel)
    {
        var name = GetString(entry, "DisplayName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var releaseType = GetString(entry, "ReleaseType");
        if (!string.IsNullOrWhiteSpace(releaseType)
            && (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (GetString(entry, "ParentKeyName") is not null)
        {
            return null;
        }

        var displayIcon = GetString(entry, "DisplayIcon");
        var iconPath = PathHeuristics.NormalizeDisplayIconPath(displayIcon);
        var installLocation = GetString(entry, "InstallLocation")?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(installLocation) && !IsPlausiblePath(installLocation))
        {
            installLocation = null;
        }

        var mainExecutable = iconPath is not null
                             && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                             && File.Exists(iconPath)
            ? iconPath
            : null;

        DateTimeOffset? installDate = null;
        var rawDate = GetString(entry, "InstallDate");
        if (!string.IsNullOrWhiteSpace(rawDate))
        {
            if (DateTime.TryParseExact(rawDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                installDate = new DateTimeOffset(parsed);
            }
            else if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                installDate = new DateTimeOffset(parsed);
            }
        }

        var estimatedKb = GetInt(entry, "EstimatedSize");
        var isSystem = GetInt(entry, "SystemComponent") == 1;
        var publisher = GetString(entry, "Publisher");
        var version = GetString(entry, "DisplayVersion");
        var uninstall = GetString(entry, "QuietUninstallString") ?? GetString(entry, "UninstallString");

        return new InstalledApp
        {
            Name = Clean(name) ?? name,
            Publisher = Clean(publisher),
            Version = Clean(version),
            InstallDate = installDate,
            InstallLocation = installLocation,
            UninstallString = uninstall,
            DisplayIcon = displayIcon,
            MainExecutablePath = mainExecutable,
            RegistryKey = $"{hiveLabel}\\{UninstallPath}\\{subKeyName}",
            EstimatedSizeBytes = estimatedKb is > 0 ? estimatedKb.Value * 1024L : null,
            IsSystemComponent = isSystem,
            Source = hiveLabel
        };
    }

    private static List<InstalledApp> Deduplicate(List<InstalledApp> apps)
    {
        var ordered = apps
            .OrderBy(a => a.Source.Contains("32", StringComparison.Ordinal) ? 1 : a.Source == "HKCU" ? 2 : 0)
            .ThenBy(a => a.RegistryKey, StringComparer.OrdinalIgnoreCase);

        var result = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in ordered)
        {
            var key = string.Join('|',
                AppIdentity.NormalizeName(app.Name),
                app.Version ?? string.Empty,
                AppIdentity.NormalizedPath(app.InstallLocation) ?? string.Empty,
                AppIdentity.NormalizePublisher(app.Publisher));
            if (seen.Add(key))
            {
                result.Add(app);
            }
        }

        return result;
    }

    public static void AssignIdentifiers(List<InstalledApp> apps)
    {
        foreach (var group in apps.GroupBy(AppIdentity.CreateId).ToList())
        {
            var ordered = group.OrderBy(a => a.RegistryKey, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Id = i == 0 ? group.Key : $"{group.Key}-{i + 1}";
            }
        }
    }

    private static bool IsPlausiblePath(string path)
    {
        if (path.Length is < 3 or > 400)
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        return path.IndexOfAny(['"', '<', '>', '|']) < 0;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string? GetString(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
            {
                string text => text.Trim(),
                string[] parts => string.Join(';', parts).Trim(),
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static int? GetInt(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) switch
            {
                int number => number,
                long number => (int)number,
                string text when int.TryParse(text, out var parsed) => parsed,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
