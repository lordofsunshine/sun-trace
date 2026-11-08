using Microsoft.Win32;

namespace SunTrace.Infrastructure.Windows;

public static class RegistryReader
{
    public static RegistryKey? OpenBase(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            return null;
        }
    }

    public static RegistryKey? OpenSubKey(string fullPath, RegistryView view = RegistryView.Default)
    {
        if (!RegistryPath.TryParse(fullPath, out var hive, out var subKey))
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(subKey, writable: false);
        }
        catch
        {
            return null;
        }
    }

    public static RegistryKey? OpenSubKeyForWrite(string fullPath)
    {
        if (!RegistryPath.TryParse(fullPath, out var hive, out var subKey))
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            return baseKey.CreateSubKey(subKey, writable: true);
        }
        catch
        {
            return null;
        }
    }

    public static object? GetValue(string path, string name, RegistryView view = RegistryView.Default)
    {
        try
        {
            using var key = OpenSubKey(path, view);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetString(string path, string name, RegistryView view = RegistryView.Default)
    {
        var value = GetValue(path, name, view);
        return value switch
        {
            null => null,
            string text => text.Trim(),
            string[] parts => string.Join(';', parts).Trim(),
            _ => value.ToString()?.Trim()
        };
    }

    public static int? GetInt(string path, string name, RegistryView view = RegistryView.Default)
    {
        var value = GetValue(path, name, view);
        return value switch
        {
            int number => number,
            long number => (int)number,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    public static bool? GetBool(string path, string name, RegistryView view = RegistryView.Default)
    {
        var value = GetInt(path, name, view);
        return value switch
        {
            0 => false,
            1 => true,
            _ => null
        };
    }

    public static byte[]? GetBinary(string path, string name, RegistryView view = RegistryView.Default)
    {
        try
        {
            using var key = OpenSubKey(path, view);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as byte[];
        }
        catch
        {
            return null;
        }
    }

    public static bool DeleteValue(string path, string name)
    {
        try
        {
            using var key = OpenSubKeyForWrite(path);
            if (key is null)
            {
                return false;
            }

            key.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool WriteBinary(string path, string name, byte[] value)
    {
        try
        {
            using var key = OpenSubKeyForWrite(path);
            key?.SetValue(name, value, RegistryValueKind.Binary);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool WriteString(string path, string name, string value)
    {
        try
        {
            using var key = OpenSubKeyForWrite(path);
            key?.SetValue(name, value, RegistryValueKind.String);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}

public static class RegistryPath
{
    public const string LocalMachine = @"\HKEY_LOCAL_MACHINE";
    public const string CurrentUser = @"\HKEY_CURRENT_USER";

    public static bool TryParse(string path, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.CurrentUser;
        subKey = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var value = path.Trim();
        if (value.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = value[5..];
            return true;
        }

        if (value.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = value[5..];
            return true;
        }

        if (value.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.ClassesRoot;
            subKey = value[5..];
            return true;
        }

        if (value.StartsWith("HKU\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.Users;
            subKey = value[4..];
            return true;
        }

        if (value.StartsWith(CurrentUser + "\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = value[(CurrentUser.Length + 1)..];
            return true;
        }

        if (value.StartsWith(LocalMachine + "\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = value[(LocalMachine.Length + 1)..];
            return true;
        }

        return false;
    }

    public static string ToDisplay(string path) =>
        path.Replace(@"\HKEY_CURRENT_USER", "HKCU", StringComparison.OrdinalIgnoreCase)
            .Replace(@"\HKEY_LOCAL_MACHINE", "HKLM", StringComparison.OrdinalIgnoreCase);
}
