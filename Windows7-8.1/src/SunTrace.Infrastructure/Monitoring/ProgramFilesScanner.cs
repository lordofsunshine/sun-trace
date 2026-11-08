using System.IO;
using System.Security.Cryptography;
using System.Text;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class FileScanResult
{
    public List<ComponentSnapshot> Components { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public bool IsComplete { get; init; }

    public int ScannedApps { get; init; }
}

public sealed class ProgramFilesScanner
{
    private const int MaxEntriesPerApp = 150;
    private const int MaxTotalEntries = 20000;
    private static readonly string[] SkippedDirectories = ["cache", "caches", "logs", "log", "temp", "tmp", ".git", "node_modules"];

    public FileScanResult Scan(IReadOnlyList<InstalledApp> apps, CancellationToken cancellationToken)
    {
        var components = new List<ComponentSnapshot>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = GetTrackedRoots();
        var complete = true;
        var scannedApps = 0;
        var skippedApps = 0;

        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PathHeuristics.IsUsableInstallLocation(app.InstallLocation))
            {
                continue;
            }

            var location = app.InstallLocation!.Trim().Trim('"').TrimEnd('\\');
            if (!Directory.Exists(location) || !IsUnderTrackedRoot(location, roots))
            {
                skippedApps++;
                continue;
            }

            scannedApps++;
            var addedForApp = 0;
            try
            {
                foreach (var entry in EnumerateTwoLevels(location, cancellationToken))
                {
                    if (addedForApp >= MaxEntriesPerApp || components.Count >= MaxTotalEntries)
                    {
                        complete = false;
                        warnings.Add("Файловый мониторинг ограничен по количеству объектов.");
                        break;
                    }

                    if (!seen.Add(entry.FullName))
                    {
                        continue;
                    }

                    components.Add(CreateComponent(entry));
                    addedForApp++;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Нет доступа к папке {location}.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Не удалось прочитать {location}: {ex.Message}");
            }

            if (!complete && components.Count >= MaxTotalEntries)
            {
                break;
            }
        }

        if (skippedApps > 0)
        {
            warnings.Add($"Файлы {skippedApps} программ находятся вне отслеживаемых папок и не проверялись.");
        }

        return new FileScanResult
        {
            Components = components,
            Warnings = warnings,
            IsComplete = complete,
            ScannedApps = scannedApps
        };
    }

    private static IEnumerable<FileSystemInfo> EnumerateTwoLevels(string location, CancellationToken cancellationToken)
    {
        IEnumerable<FileSystemInfo> rootEntries;
        try
        {
            rootEntries = new DirectoryInfo(location).EnumerateFileSystemInfos().ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var entry in rootEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;

            if (entry is not DirectoryInfo directory || ShouldSkip(directory))
            {
                continue;
            }

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = directory.EnumerateFileSystemInfos().Take(60).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child is DirectoryInfo childDirectory && ShouldSkip(childDirectory))
                {
                    continue;
                }

                yield return child;
            }
        }
    }

    private static bool ShouldSkip(DirectoryInfo directory) =>
        (directory.Attributes & FileAttributes.ReparsePoint) != 0
        || SkippedDirectories.Contains(directory.Name, StringComparer.OrdinalIgnoreCase);

    private static ComponentSnapshot CreateComponent(FileSystemInfo entry)
    {
        var isDirectory = entry is DirectoryInfo;
        long? size = entry is FileInfo file ? file.Length : null;
        var modified = new DateTimeOffset(entry.LastWriteTimeUtc, TimeSpan.Zero);
        var path = entry.FullName;
        var lower = path.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lower)))[..24].ToLowerInvariant();
        var extension = entry.Extension.ToLowerInvariant();

        return new ComponentSnapshot
        {
            Id = "file:" + hash,
            Type = isDirectory ? ComponentType.Folder : ComponentType.File,
            Name = entry.Name,
            Detail = path,
            Location = Path.GetDirectoryName(path),
            ExecutablePath = extension is ".exe" or ".dll" ? path : null,
            SizeBytes = size,
            ModifiedUtc = modified,
            Owner = "Файловая система",
            SupportsPause = false,
            Details = new Dictionary<string, string>
            {
                ["Полный путь"] = path,
                ["Тип объекта"] = isDirectory ? "Папка" : "Файл",
                ["Расширение"] = extension
            }
        };
    }

    private static List<string> GetTrackedRoots()
    {
        var roots = new List<string>();
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
        return roots;

        void Add(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                roots.Add(path.TrimEnd('\\'));
            }
        }
    }

    private static bool IsUnderTrackedRoot(string path, List<string> roots) =>
        roots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
                          || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
}
