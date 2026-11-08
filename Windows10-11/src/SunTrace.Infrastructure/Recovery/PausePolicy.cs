using System.Collections.Concurrent;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Monitoring;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Recovery;

public static class PausePolicy
{
    private const int MaxCachedTasks = 256;
    private static readonly TimeSpan TaskDecisionLifetime = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<string, (DateTime Expires, bool IsProtected)> TaskDecisions = new(StringComparer.OrdinalIgnoreCase);

    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApproveSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const int MaxApproveBytes = 64;
    private const int MaxNameLength = 260;

    public static bool TryValidate(PauseDescriptor? descriptor, out string error)
    {
        error = string.Empty;
        if (descriptor is null)
        {
            error = "Пустое описание компонента.";
            return false;
        }

        if (!HasValidOriginal(descriptor))
        {
            error = "Сохранённое состояние компонента повреждено.";
            return false;
        }

        switch (descriptor.Kind)
        {
            case PauseTargetKind.RegistryStartup:
                return ValidateRegistry(descriptor, out error);
            case PauseTargetKind.StartupFolder:
                return ValidateFolder(descriptor, out error);
            case PauseTargetKind.ScheduledTask:
                return ValidateTask(descriptor, out error);
            default:
                error = "Неизвестный тип компонента.";
                return false;
        }
    }

    private static bool ValidateRegistry(PauseDescriptor descriptor, out string error)
    {
        error = "Параметры элемента автозагрузки недопустимы.";
        var run = Normalize(descriptor.RegistryPath);
        var approve = Normalize(descriptor.ApproveRegistryPath);
        var hive = HiveOf(run);
        if (hive is null || run is null || approve is null || !IsValidValueName(descriptor.ValueName))
        {
            return false;
        }

        if (!run.Equals(hive + "\\" + RunSubKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var approveRoot = hive + "\\" + ApproveSubKey + "\\";
        var validApprove = approve.Equals(approveRoot + "Run", StringComparison.OrdinalIgnoreCase)
                           || (hive == "HKLM" && approve.Equals(approveRoot + "Run32", StringComparison.OrdinalIgnoreCase));
        if (!validApprove)
        {
            return false;
        }

        var command = RegistryReader.GetString(run, descriptor.ValueName!, PauseController.ViewFor(descriptor));
        if (command is not null)
        {
            var executable = PathHeuristics.ExtractExecutablePath(command);
            var expanded = executable is null ? null : Environment.ExpandEnvironmentVariables(executable);
            if (StartupScanner.IsProtectedPath(expanded))
            {
                error = "Системные и защитные компоненты нельзя отключать.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateFolder(PauseDescriptor descriptor, out string error)
    {
        error = "Параметры элемента автозагрузки недопустимы.";
        var approve = Normalize(descriptor.ApproveRegistryPath);
        if (string.IsNullOrWhiteSpace(descriptor.FilePath) || approve is null || !IsValidValueName(descriptor.ValueName))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(descriptor.FilePath);
        }
        catch (Exception)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(full)?.TrimEnd('\\');
        var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup).TrimEnd('\\');
        var commonFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup).TrimEnd('\\');
        string hive;
        if (directory is not null && directory.Equals(userFolder, StringComparison.OrdinalIgnoreCase))
        {
            hive = "HKCU";
        }
        else if (directory is not null && directory.Equals(commonFolder, StringComparison.OrdinalIgnoreCase))
        {
            hive = "HKLM";
        }
        else
        {
            return false;
        }

        var fileName = Path.GetFileName(full);
        if (!fileName.Equals(descriptor.ValueName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!approve.Equals(hive + "\\" + ApproveSubKey + "\\StartupFolder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            error = "Системные и защитные компоненты нельзя отключать.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateTask(PauseDescriptor descriptor, out string error)
    {
        error = "Параметры задания планировщика недопустимы.";
        var path = descriptor.TaskPath?.Trim();
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxNameLength * 2 || !path.StartsWith('\\') || path.Contains(".."))
        {
            return false;
        }

        if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Security", StringComparison.OrdinalIgnoreCase))
        {
            error = "Системные и защитные компоненты нельзя отключать.";
            return false;
        }

        if (IsProtectedTask(path))
        {
            error = "Системные и защитные компоненты нельзя отключать.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsProtectedTask(string path)
    {
        var now = DateTime.UtcNow;
        if (TaskDecisions.TryGetValue(path, out var cached) && cached.Expires > now)
        {
            return cached.IsProtected;
        }

        var isProtected = ScheduledTaskScanner.TryReadTask(path, out var task, out _)
                          && task is not null
                          && (task.IsMicrosoft || task.IsSecuritySensitive);
        if (TaskDecisions.Count >= MaxCachedTasks)
        {
            TaskDecisions.Clear();
        }

        TaskDecisions[path] = (now + TaskDecisionLifetime, isProtected);
        return isProtected;
    }

    private static bool HasValidOriginal(PauseDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.OriginalApproveValueBase64))
        {
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(descriptor.OriginalApproveValueBase64);
            return bytes.Length is > 0 and <= MaxApproveBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidValueName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= MaxNameLength
        && !name.Any(char.IsControl);

    private static string? Normalize(string? path) => path?.Trim().TrimEnd('\\');

    private static string? HiveOf(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
        {
            return "HKCU";
        }

        if (path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
        {
            return "HKLM";
        }

        return null;
    }
}
