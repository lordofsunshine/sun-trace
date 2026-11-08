using SunTrace.Core.Models;

namespace SunTrace.Core.Detection;

public static class PathHeuristics
{
    private static readonly string[] GenericLocations =
    [
        @"c:\program files", @"c:\program files (x86)", @"c:\programdata", @"c:\windows", @"c:\users", @"c:\"
    ];

    public static bool IsUsableInstallLocation(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        var normalized = installLocation.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        if (normalized.Length < 6)
        {
            return false;
        }

        return !GenericLocations.Contains(normalized);
    }

    public static bool IsUnder(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var candidate = Normalize(path);
        var root = Normalize(directory);
        if (root.Length < 4)
        {
            return false;
        }

        return candidate == root || candidate.StartsWith(root + "\\", StringComparison.Ordinal);
    }

    public static bool IsUnusualLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var value = path.ToLowerInvariant();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToLowerInvariant();
        var publicFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).ToLowerInvariant();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).ToLowerInvariant();

        string[] suspicious =
        [
            "\\temp\\", "\\tmp\\", "\\downloads\\", "\\desktop\\", "\\public\\", "\\appdata\\local\\temp\\"
        ];

        if (suspicious.Any(s => value.Contains(s, StringComparison.Ordinal)))
        {
            return true;
        }

        return value.StartsWith(userProfile + "\\appdata\\roaming", StringComparison.Ordinal)
            && !value.StartsWith(localAppData, StringComparison.Ordinal)
            && value.Contains("\\startup", StringComparison.Ordinal) is false
            && value.Contains("\\programs", StringComparison.Ordinal) is false
            && value.Contains(windows, StringComparison.Ordinal) is false
            && value.Contains(publicFolder, StringComparison.Ordinal) is false;
    }

    public static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var value = command.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1)
            {
                return value[1..end];
            }
        }

        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return value[..(exeIndex + 4)].Trim().Trim('"');
        }

        var space = value.IndexOf(' ');
        var first = space > 0 ? value[..space] : value;
        return first.Contains('\\') ? first : null;
    }

    public static string? NormalizeDisplayIconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var value = displayIcon.Trim().Trim('"');
        var comma = value.LastIndexOf(',');
        if (comma > 2 && int.TryParse(value[(comma + 1)..], out _))
        {
            value = value[..comma];
        }

        value = Environment.ExpandEnvironmentVariables(value).Trim().Trim('"');
        return value;
    }

    private static string Normalize(string path) =>
        path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
