using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Services;
using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class ProgramDetailViewModel : ObservableObject, IPageViewModel
{
    private readonly IHistoryStore _store;
    private readonly IComponentPauseService _pause;
    private readonly IReportExporter _exporter;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ProgramDetailViewModel> _logger;
    private bool _reviewed;

    [ObservableProperty]
    private string _appId = string.Empty;

    [ObservableProperty]
    private AppSummary? _app;

    [ObservableProperty]
    private InstalledApp? _appInfo;

    [ObservableProperty]
    private string _title = LocalizedStrings.Instance["Ui.ProgramDefault"];

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _versionLabel = LocalizedStrings.Instance["Ui.VersionNotSet"];

    [ObservableProperty]
    private string _installDateLabel = "-";

    [ObservableProperty]
    private string _lastUpdatedLabel = "-";

    [ObservableProperty]
    private string _lastChangeLabel = LocalizedStrings.Instance["Ui.NoEventsSince"];

    [ObservableProperty]
    private string _installLocation = string.Empty;

    [ObservableProperty]
    private string _uninstallCommand = string.Empty;

    [ObservableProperty]
    private bool _hasInstallLocation;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasComponents;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private bool _hasMoreEvents;

    [ObservableProperty]
    private int _eventCount;

    [ObservableProperty]
    private int _pausedCount;

    [ObservableProperty]
    private string _eventsLabel = LocalizedStrings.Instance["Ui.NoEvents"];

    [ObservableProperty]
    private string _componentsLabel = LocalizedStrings.Instance["Ui.NoComponents"];

    [ObservableProperty]
    private string _eventsEmptyMessage = LocalizedStrings.Instance["Ui.NoAppEvents"];

    [ObservableProperty]
    private bool _isMissing;

    public ProgramDetailViewModel(
        MainViewModel main,
        IHistoryStore store,
        IComponentPauseService pause,
        IReportExporter exporter,
        IDialogService dialogs,
        ILogger<ProgramDetailViewModel> logger)
    {
        Main = main;
        _store = store;
        _pause = pause;
        _exporter = exporter;
        _dialogs = dialogs;
        _logger = logger;
    }

    public MainViewModel Main { get; }

    public ObservableCollection<ChangeEvent> Events { get; } = new();
    public ObservableCollection<ComponentGroupViewModel> ComponentGroups { get; } = new();

    public AppStatus Status => _reviewed && App?.Status is AppStatus.Attention or AppStatus.Changed or AppStatus.New or AppStatus.Updated
        ? AppStatus.Unchanged
        : App?.Status ?? AppStatus.Unchanged;

    public string? IconPath => AppInfo?.MainExecutablePath;

    public bool ShowStatus => App is not null && Status != AppStatus.Unchanged;

    public string StatusNote => App is null
        ? string.Empty
        : _reviewed
            ? LocalizedStrings.Instance["Ui.ReviewDone"]
            : App.Status switch
    {
        AppStatus.Attention => LocalizedStrings.Instance["Ui.ReviewAttention"],
        AppStatus.Changed => LocalizedStrings.Instance["Ui.ReviewChanged"],
        AppStatus.Updated => LocalizedStrings.Instance["Ui.ReviewUpdated"],
        AppStatus.PausedComponents => LocalizedStrings.Instance["Ui.ReviewPaused"],
        AppStatus.New => LocalizedStrings.Instance["Ui.ReviewNew"],
            _ => string.Empty
        };

    public bool HasStatusNote => !string.IsNullOrWhiteSpace(StatusNote);

    public async Task LoadAsync(string appId)
    {
        AppId = appId;
        IsLoading = true;
        IsMissing = false;
        _reviewed = false;
        try
        {
            var app = await _store.GetAppAsync(appId).ConfigureAwait(true);
            if (app is null)
            {
                IsMissing = true;
                Title = LocalizedStrings.Instance["Ui.NotFound"];
                Publisher = LocalizedStrings.Instance["Ui.NotFoundMessage"];
                Events.Clear();
                ComponentGroups.Clear();
                HasComponents = false;
                HasEvents = false;
                return;
            }

            App = app;
            AppInfo = app.App;
            Title = app.Name;
            Publisher = app.Publisher;
            VersionLabel = app.App.DisplayVersion;
            InstallDateLabel = FormatDate(app.App.InstallDate);
            LastUpdatedLabel = FormatDate(app.App.LastUpdated ?? app.LastUpdateAt);
            LastChangeLabel = app.LastEventAt is { } lastEvent
                ? lastEvent.ToLocalTime().ToString("d MMMM yyyy, HH:mm", LocalizedStrings.Instance.Culture)
                : LocalizedStrings.Instance["Ui.NoEventsSince"];
            InstallLocation = app.App.InstallLocation ?? string.Empty;
            UninstallCommand = app.App.UninstallString ?? string.Empty;
            HasInstallLocation = !string.IsNullOrWhiteSpace(InstallLocation) || !string.IsNullOrWhiteSpace(AppInfo?.MainExecutablePath);
            PausedCount = app.PausedCount;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(ShowStatus));
            OnPropertyChanged(nameof(StatusNote));
            OnPropertyChanged(nameof(HasStatusNote));
            OnPropertyChanged(nameof(IconPath));

            var components = await _store.GetAppComponentsAsync(appId).ConfigureAwait(true);
            var paused = await _store.GetPausedAsync(true).ConfigureAwait(true);
            BuildGroups(components, paused.Select(p => p.ComponentId).ToHashSet(StringComparer.OrdinalIgnoreCase));

            var events = await _store.GetAppEventsAsync(appId, 0, 12).ConfigureAwait(true);
            EventCount = await _store.GetAppEventCountAsync(appId).ConfigureAwait(true);
            Events.Clear();
            foreach (var item in events)
            {
                Events.Add(ChangeEventLocalizer.Localize(item));
            }

            HasEvents = Events.Count > 0;
            HasMoreEvents = EventCount > Events.Count;
            EventsLabel = EventCount == 0
                ? LocalizedStrings.Instance["Ui.NoEvents"]
                : string.Format(LocalizedStrings.Instance["Ui.EventsCount"], EventCount);
            EventsEmptyMessage = LocalizedStrings.Instance["Ui.NoEventsSince"];

            if (app.UnreadCount > 0)
            {
                await _store.MarkAppEventsReadAsync(appId).ConfigureAwait(true);
                _reviewed = true;
                await Main.RefreshShellCountsAsync().ConfigureAwait(true);
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(ShowStatus));
                OnPropertyChanged(nameof(StatusNote));
                OnPropertyChanged(nameof(HasStatusNote));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить карточку программы.");
            EventsEmptyMessage = LocalizedStrings.Instance["Ui.ComponentEventsFailed"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(AppId))
        {
            await LoadAsync(AppId).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMoreEvents)
        {
            return;
        }

        try
        {
            var events = await _store.GetAppEventsAsync(AppId, Events.Count, 20).ConfigureAwait(true);
            foreach (var item in events)
            {
                Events.Add(ChangeEventLocalizer.Localize(item));
            }

            HasMoreEvents = EventCount > Events.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить продолжение истории.");
        }
    }
}
