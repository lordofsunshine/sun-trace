using System.ServiceProcess;
using Microsoft.Win32;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class ServiceScanResult
{
    public List<ComponentSnapshot> Components { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    public bool IsComplete { get; init; }
}

public sealed class ServiceScanner
{
    private readonly FileMetadataReader _metadata;

    public ServiceScanner(FileMetadataReader metadata)
    {
        _metadata = metadata;
    }

    public ServiceScanResult Scan(CancellationToken cancellationToken)
    {
        var components = new List<ComponentSnapshot>();
        var warnings = new List<string>();
        var complete = true;
        var states = ReadStates(warnings);

        try
        {
            using var services = RegistryReader.OpenSubKey(@"HKLM\SYSTEM\CurrentControlSet\Services", RegistryView.Registry64);
            if (services is null)
            {
                return new ServiceScanResult
                {
                    IsComplete = false,
                    Warnings = ["Не удалось прочитать список служб Windows."]
                };
            }

            foreach (var name in services.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var key = services.OpenSubKey(name, writable: false);
                    if (key is null)
                    {
                        continue;
                    }

                    var type = GetInt(key, "Type");
                    if (type is null || (type.Value & 0x30) == 0 || (type.Value & 0x3) != 0)
                    {
                        continue;
                    }

                    var start = GetInt(key, "Start");
                    if (start is null or 0 or 1)
                    {
                        continue;
                    }

                    var imagePath = GetString(key, "ImagePath");
                    var executable = ResolveExecutable(imagePath);
                    var company = _metadata.ReadVersionInfo(executable).Company;
                    var signature = _metadata.ReadSignaturePublisher(executable);
                    var displayName = GetString(key, "DisplayName") ?? name;
                    var delayed = GetInt(key, "DelayedAutoStart") == 1;
                    var isMicrosoft = IsMicrosoft(executable, company, signature);
                    states.TryGetValue(name, out var state);

                    components.Add(new ComponentSnapshot
                    {
                        Id = "service:" + name.ToLowerInvariant(),
                        Type = ComponentType.Service,
                        Name = displayName,
                        Detail = imagePath,
                        ExecutablePath = executable,
                        Publisher = company,
                        SignaturePublisher = signature,
                        Enabled = start != 4,
                        StartMode = StartModeLabel(start.Value, delayed),
                        State = StateLabel(state),
                        Owner = "Служба Windows",
                        Location = @"HKLM\SYSTEM\CurrentControlSet\Services\" + name,
                        IsMicrosoft = isMicrosoft,
                        SupportsPause = false,
                        IsSecuritySensitive = IsSecurityService(name, displayName),
                        Details = new Dictionary<string, string>
                        {
                            ["Имя службы"] = name,
                            ["Способ запуска"] = StartModeLabel(start.Value, delayed),
                            ["Состояние"] = StateLabel(state),
                            ["Запуск с задержкой"] = delayed ? "Да" : "Нет"
                        }
                    });
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception ex)
        {
            complete = false;
            warnings.Add("Не удалось прочитать список служб: " + ex.Message);
        }

        return new ServiceScanResult
        {
            Components = components.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Warnings = warnings,
            IsComplete = complete
        };
    }

    private static Dictionary<string, ServiceControllerStatus> ReadStates(List<string> warnings)
    {
        var result = new Dictionary<string, ServiceControllerStatus>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var controller in ServiceController.GetServices())
            {
                using (controller)
                {
                    try
                    {
                        result[controller.ServiceName] = controller.Status;
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add("Текущее состояние служб недоступно: " + ex.Message);
        }

        return result;
    }

    private static string? ResolveExecutable(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var value = imagePath.Trim();
        if (value.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), value[12..]);
        }
        else if (value.StartsWith("System32\\", StringComparison.OrdinalIgnoreCase))
        {
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), value);
        }

        var path = PathHeuristics.ExtractExecutablePath(value);
        if (path is null)
        {
            return null;
        }

        path = Environment.ExpandEnvironmentVariables(path);
        return path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path)
            : path;
    }

    private static string StartModeLabel(int start, bool delayed) => start switch
    {
        0 => "Загрузка системы",
        1 => "Загрузка системы",
        2 => delayed ? "Автоматически (с задержкой)" : "Автоматически",
        3 => "Вручную",
        4 => "Отключена",
        _ => "Неизвестно"
    };

    private static string StateLabel(ServiceControllerStatus? status) => status switch
    {
        ServiceControllerStatus.Running => "Работает",
        ServiceControllerStatus.Stopped => "Остановлена",
        ServiceControllerStatus.StartPending => "Запускается",
        ServiceControllerStatus.StopPending => "Останавливается",
        ServiceControllerStatus.Paused => "Приостановлена",
        ServiceControllerStatus.PausePending => "Приостанавливается",
        ServiceControllerStatus.ContinuePending => "Продолжается",
        _ => "Неизвестно"
    };

    private static bool IsMicrosoft(string? executable, string? company, string? signature)
    {
        if (signature?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
            || company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return executable.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSecurityService(string name, string displayName)
    {
        var value = (name + " " + displayName).ToLowerInvariant();
        string[] markers = ["defender", "windefend", "securityhealth", "sense", "kaspersky", "eset", "avast", "avg", "bitdefender", "norton", "mcafee"];
        return markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private static string? GetString(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
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
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
