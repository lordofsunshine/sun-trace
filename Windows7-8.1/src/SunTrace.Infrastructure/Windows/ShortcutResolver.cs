using System.Collections.Concurrent;

namespace SunTrace.Infrastructure.Windows;

public sealed class ShortcutResolver
{
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? ResolveTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        var key = $"{path}|{stamp}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string? target = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(path);
                var raw = shortcut.TargetPath as string;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    target = Environment.ExpandEnvironmentVariables(raw);
                }
            }
        }
        catch
        {
            target = null;
        }

        _cache[key] = target;
        return target;
    }
}
