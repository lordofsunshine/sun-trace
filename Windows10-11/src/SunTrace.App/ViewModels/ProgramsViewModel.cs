using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.App.Services;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public sealed record SortOption(string Key, string Label);

public sealed record AppRow(IReadOnlyList<AppSummary> Items, int Columns);

public partial class ProgramsViewModel : ObservableObject, IPageViewModel
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly IHistoryStore _store;
    private readonly ILogger<ProgramsViewModel> _logger;
    private readonly Debouncer _searchDebouncer = new(SearchDelay);
    private IReadOnlyList<AppSummary> _apps = Array.Empty<AppSummary>();
    private bool _suspendRebuild;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filter = "All";

    [ObservableProperty]
    private string _sort = "Name";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _columns = 3;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private int _allCount;

    [ObservableProperty]
    private int _changedCount;

    [ObservableProperty]
    private int _attentionCount;

    [ObservableProperty]
    private int _pausedCount;

    [ObservableProperty]
    private bool _hasApps;

    [ObservableProperty]
    private IReadOnlyList<AppRow> _rows = Array.Empty<AppRow>();

    [ObservableProperty]
    private string _emptyTitle = LocalizedStrings.Instance["Ui.NoPrograms"];

    [ObservableProperty]
    private string _emptyMessage = LocalizedStrings.Instance["Ui.NoProgramsMessage"];

    public ProgramsViewModel(MainViewModel main, IHistoryStore store, ILogger<ProgramsViewModel> logger)
    {
        Main = main;
        _store = store;
        _logger = logger;
        SortOptions = BuildSortOptions();
        LocalizedStrings.Instance.LanguageChanged += OnLanguageChanged;
    }

    public MainViewModel Main { get; }

    public IReadOnlyList<SortOption> SortOptions { get; private set; }

    public string CountLabel => FilteredCount == TotalCount
        ? string.Format(LocalizedStrings.Instance["Ui.ProgramsCountFormat"], TotalCount)
        : string.Format(LocalizedStrings.Instance["Ui.FilteredOfTotalFormat"], FilteredCount, TotalCount);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _apps = await _store.GetAppsAsync(cancellationToken).ConfigureAwait(true);
            TotalCount = _apps.Count;
            AllCount = _apps.Count;
            ChangedCount = _apps.Count(a => a.Status is AppStatus.Changed or AppStatus.Attention);
            AttentionCount = _apps.Count(a => a.Status == AppStatus.Attention);
            PausedCount = _apps.Count(a => a.PausedCount > 0);
            Rebuild();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить список программ.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SetFilter(string? value) =>
        Filter = value is "Changed" or "Attention" or "Paused" ? value : "All";

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendRebuild = true;
        SearchText = string.Empty;
        Filter = "All";
        Sort = "Name";
        _suspendRebuild = false;
        _searchDebouncer.Cancel();
        Rebuild();
    }

    [RelayCommand]
    private async Task OpenAppAsync(AppSummary? app)
    {
        if (app is not null)
        {
            await Main.OpenAppAsync(app.Id).ConfigureAwait(true);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suspendRebuild)
        {
            _searchDebouncer.Run(Rebuild);
        }
    }

    partial void OnFilterChanged(string value) => RebuildUnlessSuspended();

    partial void OnSortChanged(string value) => RebuildUnlessSuspended();

    partial void OnColumnsChanged(int value) => RebuildUnlessSuspended();

    private void RebuildUnlessSuspended()
    {
        if (!_suspendRebuild)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var visible = Order(_apps.Where(Matches)).ToList();
        FilteredCount = visible.Count;
        HasApps = visible.Count > 0;
        var columns = Math.Max(1, Columns);
        Rows = visible.Chunk(columns).Select(chunk => new AppRow(chunk, columns)).ToList();
        UpdateEmptyState();
        OnPropertyChanged(nameof(CountLabel));
    }

    private IEnumerable<AppSummary> Order(IEnumerable<AppSummary> apps) => Sort switch
    {
        "Recent" => apps.OrderByDescending(a => a.LastChangeAt),
        "Status" => apps.OrderByDescending(a => a.Status).ThenByDescending(a => a.LastChangeAt),
        _ => apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
    };

    private bool Matches(AppSummary app)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            var matches = app.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                          || app.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase)
                          || (app.App.Version?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!matches)
            {
                return false;
            }
        }

        return Filter switch
        {
            "Changed" => app.Status is AppStatus.Changed or AppStatus.Attention,
            "Attention" => app.Status == AppStatus.Attention,
            "Paused" => app.PausedCount > 0,
            _ => true
        };
    }

    private void UpdateEmptyState()
    {
        if (HasApps)
        {
            return;
        }

        var noPrograms = TotalCount == 0;
        EmptyTitle = LocalizedStrings.Instance[noPrograms ? "Ui.NoPrograms" : "Ui.NoResults"];
        EmptyMessage = LocalizedStrings.Instance[noPrograms ? "Ui.NoProgramsMessage" : "Ui.NoResultsMessage"];
    }

    private static IReadOnlyList<SortOption> BuildSortOptions() => new[]
    {
        new SortOption("Name", LocalizedStrings.Instance["Ui.SortName"]),
        new SortOption("Recent", LocalizedStrings.Instance["Ui.SortRecent"]),
        new SortOption("Status", LocalizedStrings.Instance["Ui.SortStatus"])
    };

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SortOptions = BuildSortOptions();
        OnPropertyChanged(nameof(SortOptions));
        OnPropertyChanged(nameof(CountLabel));
        UpdateEmptyState();
    }
}
