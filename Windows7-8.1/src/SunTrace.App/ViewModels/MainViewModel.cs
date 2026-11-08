using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.App.Services;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Monitoring;

namespace SunTrace.App.ViewModels;

public enum AppSection
{
    Overview,
    Programs,
    History,
    Paused,
    Settings
}

public interface IPageViewModel
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Title { get; init; } = "SunTrace";

    public string Message { get; init; } = string.Empty;

    public string? AppId { get; init; }
}

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IHistoryStore _store;
    private readonly MonitoringService _monitoring;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _searchCancellation;
    private bool _initialized;
    private bool _disposed;
    private int _toastVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionName))]
    private AppSection _currentSection = AppSection.Overview;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _pageTitle = LocalizedStrings.Instance["Shell.Overview"];

    [ObservableProperty]
    private string _pageSubtitle = LocalizedStrings.Instance["Shell.OverviewSubtitle"];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanStatus = LocalizedStrings.Instance["Ui.Ready"];

    [ObservableProperty]
    private double _scanPercent;

    [ObservableProperty]
    private string _scanPercentText = string.Empty;

    [ObservableProperty]
    private bool _isMonitoringEnabled = true;

    [ObservableProperty]
    private string _monitoringLabel = LocalizedStrings.Instance["Ui.MonitoringOn"];

    [ObservableProperty]
    private string _nextScanLabel = LocalizedStrings.Instance["Ui.Waiting"];

    [ObservableProperty]
    private int _pausedCount;

    [ObservableProperty]
    private int _attentionCount;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _sectionBadge = string.Empty;

    public MainViewModel(
        IServiceProvider services,
        ISettingsService settings,
        IHistoryStore store,
        MonitoringService monitoring,
        IDialogService dialogs,
        ILogger<MainViewModel> logger)
    {
        _services = services;
        _settings = settings;
        _store = store;
        _monitoring = monitoring;
        _dialogs = dialogs;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        LocalizedStrings.Instance.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler<NotificationEventArgs>? NotificationRequested;
    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public string CurrentSectionName => CurrentSection.ToString();
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool IsSearchEmpty => IsSearchOpen && !IsSearching && !HasSearchResults && !string.IsNullOrWhiteSpace(SearchText);

    public AppSettings WindowSettings => _settings.Current;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await _settings.LoadAsync().ConfigureAwait(true);
            _monitoring.ScanCompleted += OnScanCompleted;
            _monitoring.StateChanged += OnMonitoringStateChanged;
            _settings.SettingsChanged += OnSettingsChanged;

            await _store.InitializeAsync().ConfigureAwait(true);

            UpdateMonitoringState();
            await NavigateAsync(nameof(AppSection.Overview)).ConfigureAwait(true);
            await RefreshShellCountsAsync().ConfigureAwait(true);
            await _monitoring.InitializeAsync().ConfigureAwait(true);
            UpdateMonitoringState();
            await RefreshShellCountsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось инициализировать приложение.");
            ScanStatus = LocalizedStrings.Instance["Ui.InitializationError"];
            ShowToast(LocalizedStrings.Instance["Ui.MonitoringStartFailed"]);
        }
    }
}
