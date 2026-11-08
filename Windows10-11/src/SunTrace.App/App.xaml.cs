using System.Windows;
using System.Windows.Threading;
#if !LEGACY_WINDOWS
using System.Text.RegularExpressions;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if !LEGACY_WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif
using SunTrace.App.Services;
using SunTrace.App.Theme;
using SunTrace.App.Localization;
using SunTrace.App.ViewModels;
using SunTrace.Core.Interfaces;
using SunTrace.Infrastructure;
using SunTrace.Infrastructure.Recovery;
using SunTrace.Infrastructure.Services;

namespace SunTrace.App;

public partial class App : Application
{
    private const string ActivationEventName = @"Local\SunTrace.Activate";
#if !LEGACY_WINDOWS
    private static readonly object PendingToastLock = new();
    private static string? _pendingToastAppId;
    private static bool _hasPendingToast;
#endif
    private bool _isReady;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;

#if !LEGACY_WINDOWS
    static App()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }
#endif

    public static IServiceProvider? Services { get; private set; }

    public static bool StartMinimized { get; private set; }

    public static bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SessionEnding += (_, _) =>
        {
            IsShuttingDown = true;
            if (MainWindow is MainWindow window)
            {
                window.PrepareForShutdown();
            }
        };

        if (ElevationHost.IsElevationRequest(e.Args, out var payload) && payload is not null)
        {
            Shutdown(ElevationHost.ExecuteFromCommandLine(payload));
            return;
        }

        StartMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, @"Local\SunTrace.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!SignalFirstInstance())
            {
                MessageBox.Show(LocalizedStrings.Instance["Ui.AlreadyRunningMessage"], "SunTrace", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        StartActivationListener();

        ILogger<App>? logger = null;
        try
        {
            var paths = new DataPaths();
            paths.EnsureCreated();

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddProvider(new FileLoggerProvider(paths.LogsDirectory));
            });
            services.AddSunTraceInfrastructure(paths);
            services.AddSingleton<ThemeManager>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<OverviewViewModel>();
            services.AddSingleton<ProgramsViewModel>();
            services.AddSingleton<ProgramDetailViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddSingleton<PausedViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<TrackingDialogViewModel>();

            Services = services.BuildServiceProvider();

            var log = Services.GetRequiredService<ILogger<App>>();
            logger = log;
            DispatcherUnhandledException += (_, args) =>
            {
                log.LogError(args.Exception, "Необработанная ошибка интерфейса.");
                args.Handled = true;
                MessageBox.Show(LocalizedStrings.Instance["Ui.UnexpectedError"] + "\n\n" + args.Exception.Message,
                    "SunTrace", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    log.LogCritical(exception, "Необработанная ошибка приложения.");
                }
            };

            var settings = Services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);

            LocalizedStrings.Instance.Apply(settings.Current.Language);

            var theme = Services.GetRequiredService<ThemeManager>();
            theme.Apply(settings.Current.Theme);
            theme.StartWatching();

            var main = Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(main);
            MainWindow = window;
            window.Show();
            if (StartMinimized)
            {
                window.Hide();
            }

            await main.InitializeAsync().ConfigureAwait(true);
            _isReady = true;
#if !LEGACY_WINDOWS
            await ProcessPendingToastAsync(window).ConfigureAwait(true);
#endif
        }
        catch (Exception ex)
        {
            logger?.LogCritical(ex, "Не удалось запустить SunTrace.");
            MessageBox.Show(LocalizedStrings.Instance["Ui.StartupFailed"] + ex.Message, "SunTrace", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool SignalFirstInstance()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ActivationEventName);
            return handle.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void StartActivationListener()
    {
        try
        {
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        }
        catch (Exception)
        {
            return;
        }

        var handle = _activationEvent;
        var thread = new Thread(() =>
        {
            try
            {
                while (!IsShuttingDown)
                {
                    if (!handle.WaitOne(500))
                    {
                        continue;
                    }

                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        if (_isReady && MainWindow is MainWindow window)
                        {
                            window.ActivateFromExternal();
                        }
                    });
                }
            }
            catch (ObjectDisposedException)
            {
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    public static void RequestShutdown()
    {
        IsShuttingDown = true;
        Current?.Shutdown();
    }

#if !LEGACY_WINDOWS
    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        string? appId = null;
        var match = Regex.Match(args.Argument ?? string.Empty, @"(?:^|&)appId=([^&]+)");
        if (match.Success)
        {
            appId = Uri.UnescapeDataString(match.Groups[1].Value);
        }

        var app = Current as App;
        if (app is null)
        {
            StorePendingToast(appId);
            return;
        }

        app.Dispatcher.InvokeAsync(() => app.HandleToastActivation(appId));
    }

    private void HandleToastActivation(string? appId)
    {
        StorePendingToast(appId);
        if (!_isReady || MainWindow is not MainWindow window)
        {
            return;
        }

        _ = ProcessPendingToastAsync(window);
    }

    private static void StorePendingToast(string? appId)
    {
        lock (PendingToastLock)
        {
            _pendingToastAppId = appId;
            _hasPendingToast = true;
        }
    }

    private async Task ProcessPendingToastAsync(MainWindow window)
    {
        string? appId;
        lock (PendingToastLock)
        {
            if (!_hasPendingToast)
            {
                return;
            }

            appId = _pendingToastAppId;
            _hasPendingToast = false;
            _pendingToastAppId = null;
        }

        try
        {
            await window.OpenFromNotificationAsync(appId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (Services?.GetService(typeof(ILogger<App>)) is ILogger<App> logger)
            {
                logger.LogError(ex, "Не удалось открыть приложение из уведомления.");
            }
        }
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        if (Services?.GetService(typeof(ThemeManager)) is ThemeManager theme)
        {
            theme.StopWatching();
        }

        if (Services is IAsyncDisposable disposable)
        {
            disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else
        {
            (Services as IDisposable)?.Dispose();
        }

        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
