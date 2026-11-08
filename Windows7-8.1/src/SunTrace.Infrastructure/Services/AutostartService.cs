using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SunTrace.Infrastructure.Services;

public sealed class AutostartService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SunTrace";

    private readonly ILogger<AutostartService> _logger;

    public AutostartService(ILogger<AutostartService> logger)
    {
        _logger = logger;
    }

    public string? GetRegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(GetRegisteredCommand());

    public bool SetEnabled(bool enabled, string? executablePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var path = executablePath ?? Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            key.SetValue(ValueName, $"\"{path}\" --minimized", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось изменить автозапуск SunTrace.");
            return false;
        }
    }
}
