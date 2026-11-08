using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SunTrace.App.Services;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public sealed class TimelineChangeItem
{
    public string AppName { get; init; } = string.Empty;

    public string ChangeLabel { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public ChangeKind Kind { get; init; }

    public ComponentType Type { get; init; }

    public string TypeLabel => Type switch
    {
        ComponentType.Application => LocalizedStrings.Instance["Ui.ComponentProgram"],
        ComponentType.Startup => LocalizedStrings.Instance["Ui.ComponentStartup"],
        ComponentType.Service => LocalizedStrings.Instance["Ui.ComponentService"],
        ComponentType.ScheduledTask => LocalizedStrings.Instance["Ui.ComponentTask"],
        ComponentType.File => LocalizedStrings.Instance["Ui.ComponentFile"],
        ComponentType.Folder => LocalizedStrings.Instance["Ui.ComponentFolder"],
        ComponentType.Registry => LocalizedStrings.Instance["Ui.ComponentRegistry"],
        _ => LocalizedStrings.Instance["Ui.ComponentGeneric"]
    };
}

public partial class HistoryViewModel : ObservableObject, IPageViewModel
{
    private readonly IHistoryStore _store;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly SnapshotComparer _comparer = new();
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(250));
    private readonly Debouncer _filterDebouncer = new(TimeSpan.FromMilliseconds(30));
    private string? _appFilterId;
    private string _appFilterName = string.Empty;
    private int _loadVersion;
    private bool _suppressCompare;
    private bool _suspendRefresh;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _kindFilter = "All";

    [ObservableProperty]
    private string _typeFilter = "All";

    [ObservableProperty]
    private string _importanceFilter = "All";

    [ObservableProperty]
    private string _period = "All";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEventsMode))]
    private bool _isRewindMode;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private string _totalLabel = LocalizedStrings.Instance["Ui.NoEventsYet"];

    [ObservableProperty]
    private string _emptyTitle = LocalizedStrings.Instance["Ui.HistoryEmptyTitle"];

    [ObservableProperty]
    private string _emptyMessage = LocalizedStrings.Instance["Ui.HistoryEmptyMessageLong"];

    [ObservableProperty]
    private bool _hasAppFilter;

    [ObservableProperty]
    private string _appFilterLabel = string.Empty;

    [ObservableProperty]
    private bool _hasSnapshots;

    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private string _rewindSummary = string.Empty;

    [ObservableProperty]
    private string _rewindEmptyMessage = LocalizedStrings.Instance["Ui.RewindNeedsSnapshots"];

    [ObservableProperty]
    private SnapshotSummary? _fromSnapshot;

    [ObservableProperty]
    private SnapshotSummary? _toSnapshot;

    [ObservableProperty]
    private bool _hasTimelineChanges;

    [ObservableProperty]
    private string _rewindCounts = string.Empty;

    public HistoryViewModel(MainViewModel main, IHistoryStore store, ILogger<HistoryViewModel> logger)
    {
        Main = main;
        _store = store;
        _logger = logger;
        EventsView = CollectionViewSource.GetDefaultView(Events);
        EventsView.GroupDescriptions.Add(new PropertyGroupDescription("DetectedAt.Date"));
    }

    public MainViewModel Main { get; }

    public ObservableCollection<ChangeEvent> Events { get; } = new();

    public ICollectionView EventsView { get; }

    public ObservableCollection<SnapshotSummary> Snapshots { get; } = new();
    public ObservableCollection<TimelineChangeItem> TimelineChanges { get; } = new();
    public bool IsEventsMode => !IsRewindMode;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRewindMode)
        {
            await LoadSnapshotsAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await ReloadAsync(cancellationToken).ConfigureAwait(true);
        }
    }
}
