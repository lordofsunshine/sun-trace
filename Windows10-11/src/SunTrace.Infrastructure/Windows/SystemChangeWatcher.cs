using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SunTrace.Infrastructure.Windows;

public sealed class SystemChangeWatcher : IDisposable
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";

    private static readonly (RegistryHive Hive, RegistryView View, string Path, bool Subtree)[] RegistryTargets =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersion + @"\Uninstall", true),
        (RegistryHive.LocalMachine, RegistryView.Registry32, CurrentVersion + @"\Uninstall", true),
        (RegistryHive.CurrentUser, RegistryView.Registry64, CurrentVersion + @"\Uninstall", true),
        (RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersion + @"\Run", false),
        (RegistryHive.LocalMachine, RegistryView.Registry32, CurrentVersion + @"\Run", false),
        (RegistryHive.CurrentUser, RegistryView.Registry64, CurrentVersion + @"\Run", false),
        (RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersion + @"\Explorer\StartupApproved", true),
        (RegistryHive.CurrentUser, RegistryView.Registry64, CurrentVersion + @"\Explorer\StartupApproved", true),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services", false),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree", true)
    ];

    private readonly ILogger<SystemChangeWatcher> _logger;
    private readonly object _sync = new();
    private readonly List<IDisposable> _watches = new();
    private bool _disposed;

    public SystemChangeWatcher(ILogger<SystemChangeWatcher> logger)
    {
        _logger = logger;
    }

    public event EventHandler? Changed;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _watches.Count > 0;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _watches.Count > 0)
            {
                return;
            }

            foreach (var target in RegistryTargets)
            {
                var watch = RegistryWatch.TryCreate(target.Hive, target.View, target.Path, target.Subtree, RaiseChanged);
                if (watch is not null)
                {
                    _watches.Add(watch);
                }
            }

            foreach (var folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                     })
            {
                var watch = TryWatchDirectory(folder);
                if (watch is not null)
                {
                    _watches.Add(watch);
                }
            }

            _logger.LogInformation("Отслеживание изменений системы запущено. Источников: {Count}.", _watches.Count);
        }
    }

    public void Stop()
    {
        List<IDisposable> watches;
        lock (_sync)
        {
            watches = new List<IDisposable>(_watches);
            _watches.Clear();
        }

        foreach (var watch in watches)
        {
            watch.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private FileSystemWatcher? TryWatchDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };
            watcher.Created += (_, _) => RaiseChanged();
            watcher.Deleted += (_, _) => RaiseChanged();
            watcher.Renamed += (_, _) => RaiseChanged();
            watcher.Changed += (_, _) => RaiseChanged();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось отслеживать папку {Path}.", path);
            return null;
        }
    }

    private sealed class RegistryWatch : IDisposable
    {
        private const uint NotifyChangeName = 0x1;
        private const uint NotifyChangeLastSet = 0x4;
        private const uint NotifyThreadAgnostic = 0x10000000;
        private const uint Filter = NotifyChangeName | NotifyChangeLastSet | NotifyThreadAgnostic;

        private readonly RegistryKey _key;
        private readonly bool _subtree;
        private readonly Action _onChange;
        private readonly ManualResetEvent _signal = new(false);
        private readonly object _sync = new();
        private RegisteredWaitHandle? _registration;
        private bool _disposed;

        private RegistryWatch(RegistryKey key, bool subtree, Action onChange)
        {
            _key = key;
            _subtree = subtree;
            _onChange = onChange;
        }

        public static RegistryWatch? TryCreate(RegistryHive hive, RegistryView view, string path, bool subtree, Action onChange)
        {
            RegistryKey? key = null;
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                key = root.OpenSubKey(path, writable: false);
                if (key is null)
                {
                    return null;
                }

                var watch = new RegistryWatch(key, subtree, onChange);
                if (watch.Arm())
                {
                    return watch;
                }

                watch.Dispose();
                return null;
            }
            catch (Exception)
            {
                key?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _registration?.Unregister(null);
                _registration = null;
                _signal.Dispose();
                _key.Dispose();
            }
        }

        private bool Arm()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _signal.Reset();
                if (RegNotifyChangeKeyValue(_key.Handle, _subtree, Filter, _signal.SafeWaitHandle, true) != 0)
                {
                    return false;
                }

                _registration = ThreadPool.RegisterWaitForSingleObject(_signal, OnSignaled, null, Timeout.Infinite, true);
                return true;
            }
        }

        private void OnSignaled(object? state, bool timedOut)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                _onChange();
            }
            catch (Exception)
            {
            }

            if (!Arm())
            {
                Dispose();
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(
            SafeRegistryHandle hKey,
            [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
            uint notifyFilter,
            SafeWaitHandle hEvent,
            [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
    }
}
