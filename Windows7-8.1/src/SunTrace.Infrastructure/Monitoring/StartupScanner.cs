using Microsoft.Win32;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class StartupScanResult
{
    public List<ComponentSnapshot> Components { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public bool IsComplete { get; init; }
}

public sealed class StartupScanner
{
    private const string ApproveRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string RunRoot = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly FileMetadataReader _metadata;
    private readonly ShortcutResolver _shortcuts;

    private static readonly RegistryLocation[] RegistryLocations =
    [
        new(@"HKCU\" + RunRoot, RegistryView.Registry64, @"HKCU\" + ApproveRoot + @"\Run", "Автозагрузка пользователя", false),
        new(@"HKLM\" + RunRoot, RegistryView.Registry64, @"HKLM\" + ApproveRoot + @"\Run", "Автозагрузка для всех пользователей", true),
        new(@"HKLM\" + RunRoot, RegistryView.Registry32, @"HKLM\" + ApproveRoot + @"\Run32", "Автозагрузка для всех пользователей (32-бит)", true)
    ];

    public StartupScanner(FileMetadataReader metadata, ShortcutResolver shortcuts)
    {
        _metadata = metadata;
        _shortcuts = shortcuts;
    }

    public StartupScanResult Scan(CancellationToken cancellationToken)
    {
        var components = new List<ComponentSnapshot>();
        var warnings = new List<string>();
        var complete = true;

        foreach (var location in RegistryLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var key = RegistryReader.OpenSubKey(location.RunKey, location.RunView);
                if (key is null)
                {
                    complete = false;
                    warnings.Add($"Раздел {RegistryPath.ToDisplay(location.RunKey)} недоступен.");
                    continue;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(valueName))
                    {
                        continue;
                    }

                    var command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    components.Add(CreateRegistryEntry(location, valueName, command));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                complete = false;
                warnings.Add($"Нет доступа к {RegistryPath.ToDisplay(location.RunKey)}.");
            }
            catch (Exception ex)
            {
                complete = false;
                warnings.Add($"Ошибка чтения автозагрузки: {ex.Message}");
            }
        }

        AddStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            @"HKCU\" + ApproveRoot + @"\StartupFolder",
            "Папка автозагрузки пользователя",
            requiresElevation: false,
            components,
            warnings,
            ref complete,
            cancellationToken);

        AddStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            @"HKLM\" + ApproveRoot + @"\StartupFolder",
            "Общая папка автозагрузки",
            requiresElevation: true,
            components,
            warnings,
            ref complete,
            cancellationToken);

        return new StartupScanResult
        {
            Components = components.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Warnings = warnings,
            IsComplete = complete
        };
    }

    private ComponentSnapshot CreateRegistryEntry(RegistryLocation location, string valueName, string command)
    {
        var enabled = ReadEnabled(location.ApproveKey, valueName, out var approveValue);
        var executable = PathHeuristics.ExtractExecutablePath(command);
        var expanded = executable is null ? null : Environment.ExpandEnvironmentVariables(executable);
        var company = _metadata.ReadVersionInfo(expanded).Company;
        var signature = _metadata.ReadSignaturePublisher(expanded);
        var isMicrosoft = IsMicrosoft(expanded, company, signature);
        var supportsPause = enabled && !isMicrosoft && !IsWindowsPath(expanded) && !IsSecurityComponent(expanded);

        return new ComponentSnapshot
        {
            Id = BuildId(location.ScopeKey + "|" + valueName + "|" + location.RunKey),
            Type = ComponentType.Startup,
            Name = valueName,
            Detail = command,
            ExecutablePath = expanded,
            Publisher = company,
            SignaturePublisher = signature,
            Enabled = enabled,
            Owner = location.Owner,
            Location = RegistryPath.ToDisplay(location.RunKey),
            IsMicrosoft = isMicrosoft,
            SupportsPause = supportsPause,
            Details = new Dictionary<string, string>
            {
                ["Область"] = location.RequiresElevation ? "Все пользователи" : "Текущий пользователь",
                ["Имя файла"] = expanded is null ? valueName : Path.GetFileName(expanded)
            },
            Pause = new PauseDescriptor
            {
                Kind = PauseTargetKind.RegistryStartup,
                RegistryPath = location.RunKey,
                ApproveRegistryPath = location.ApproveKey,
                ValueName = valueName,
                OriginalEnabled = enabled,
                OriginalApproveValueExisted = approveValue is not null,
                OriginalApproveValueBase64 = approveValue is null ? null : Convert.ToBase64String(approveValue)
            }
        };
    }

    private void AddStartupFolder(
        string folder,
        string approveKey,
        string owner,
        bool requiresElevation,
        List<ComponentSnapshot> components,
        List<string> warnings,
        ref bool complete,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var enabled = ReadEnabled(approveKey, fileName, out var approveValue);
                var target = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? _shortcuts.ResolveTarget(file)
                    : file;
                var company = _metadata.ReadVersionInfo(target).Company;
                var signature = _metadata.ReadSignaturePublisher(target);
                var isMicrosoft = IsMicrosoft(target, company, signature) || fileName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
                var supportsPause = enabled && !isMicrosoft && !IsWindowsPath(target) && !IsSecurityComponent(target);

                components.Add(new ComponentSnapshot
                {
                    Id = BuildId("folder|" + owner + "|" + fileName),
                    Type = ComponentType.Startup,
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    Detail = file,
                    ExecutablePath = target,
                    Publisher = company,
                    SignaturePublisher = signature,
                    Enabled = enabled,
                    Owner = owner,
                    Location = folder,
                    IsMicrosoft = isMicrosoft,
                    SupportsPause = supportsPause,
                    Details = new Dictionary<string, string>
                    {
                        ["Область"] = owner,
                        ["Имя файла"] = fileName,
                        ["Цель ярлыка"] = target ?? "не определена"
                    },
                    Pause = new PauseDescriptor
                    {
                        Kind = PauseTargetKind.StartupFolder,
                        FilePath = file,
                        ApproveRegistryPath = approveKey,
                        ValueName = fileName,
                        OriginalEnabled = enabled,
                        OriginalApproveValueExisted = approveValue is not null,
                        OriginalApproveValueBase64 = approveValue is null ? null : Convert.ToBase64String(approveValue)
                    }
                });
            }
        }
        catch (Exception ex)
        {
            complete = false;
            warnings.Add($"Не удалось прочитать {folder}: {ex.Message}");
        }
    }

    private static bool ReadEnabled(string approveKey, string valueName, out byte[]? value)
    {
        value = RegistryReader.GetBinary(approveKey, valueName, RegistryView.Registry64);
        if (value is null || value.Length == 0)
        {
            return true;
        }

        return value[0] is not (3 or 7 or 9);
    }

    private static string BuildId(string seed)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed.ToLowerInvariant())))[..20].ToLowerInvariant();
        return "startup:" + hash;
    }

    public static bool IsProtectedPath(string? path) => IsWindowsPath(path) || IsSecurityComponent(path);

    private static bool IsWindowsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMicrosoft(string? path, string? company, string? signature)
    {
        if (!string.IsNullOrWhiteSpace(signature) && signature.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(company) && company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path is not null && IsWindowsPath(path);
    }

    private static bool IsSecurityComponent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var value = path.ToLowerInvariant();
        string[] markers =
        [
            "\\windows defender", "\\securityhealth", "\\msmpeng", "\\sense\\", "\\windefend",
            "\\kaspersky", "\\eset\\", "\\avast", "\\avg\\", "\\bitdefender", "\\norton", "\\mcafee"
        ];
        return markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private sealed record RegistryLocation(string RunKey, RegistryView RunView, string ApproveKey, string Owner, bool RequiresElevation)
    {
        public string ScopeKey => RunKey + "|" + RunView;
    }
}
