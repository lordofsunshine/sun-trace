using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.App.Localization;
using SunTrace.App.Services;

namespace SunTrace.App.ViewModels;

public partial class OverviewViewModel : ObservableObject, IPageViewModel
{
    private readonly IHistoryStore _store;
    private readonly ILogger<OverviewViewModel> _logger;

    [ObservableProperty]
    private DashboardStats _stats = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _lastScanLabel = LocalizedStrings.Instance["Ui.NotScannedYet"];

    [ObservableProperty]
    private string _changesSummary = LocalizedStrings.Instance["Ui.NoEventsLast7"];

    [ObservableProperty]
    private string _attentionSummary = LocalizedStrings.Instance["Ui.AllCalm"];

    [ObservableProperty]
    private bool _hasRecentEvents;

    [ObservableProperty]
    private bool _hasAttention;

    [ObservableProperty]
    private bool _hasTrackedComponents;

    [ObservableProperty]
    private string _lastScanDisplay = string.Empty;

    [ObservableProperty]
    private string _snapshotsSavedLabel = string.Empty;

    public OverviewViewModel(MainViewModel main, IHistoryStore store, ILogger<OverviewViewModel> logger)
    {
        Main = main;
        _store = store;
        _logger = logger;
    }

    public MainViewModel Main { get; }

    public ObservableCollection<ChangeEvent> RecentEvents { get; } = new();

    public ObservableCollection<AppSummary> AttentionItems { get; } = new();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var stats = await _store.GetDashboardStatsAsync(cancellationToken).ConfigureAwait(true);
            var events = await _store.QueryEventsAsync(new EventQuery { PageSize = 5 }, cancellationToken).ConfigureAwait(true);
            var apps = await _store.GetAppsAsync(cancellationToken).ConfigureAwait(true);

            Stats = stats;
            HasTrackedComponents = stats.StartupCount > 0 || stats.ServiceCount > 0 || stats.TaskCount > 0;
            LastScanLabel = stats.LastScanAt is { } lastScan
                ? lastScan.ToLocalTime().ToString("d MMMM, HH:mm", LocalizedStrings.Instance.Culture)
                : LocalizedStrings.Instance["Ui.NotScannedYet"];
            LastScanDisplay = $"{LocalizedStrings.Instance["Ui.LastCheck"]}: {LastScanLabel}";
            SnapshotsSavedLabel = string.Format(LocalizedStrings.Instance["Ui.SnapshotsSaved"], stats.SnapshotCount);

            ChangesSummary = stats.EventsLast7Days == 0
                ? LocalizedStrings.Instance["Ui.NoEventsLast7"]
                : string.Format(LocalizedStrings.Instance["Ui.EventsLast7Format"], stats.EventsLast7Days);

            RecentEvents.Clear();
            foreach (var item in events.Items)
            {
                RecentEvents.Add(ChangeEventLocalizer.Localize(item));
            }

            HasRecentEvents = RecentEvents.Count > 0;

            AttentionItems.Clear();
            foreach (var app in apps
                         .Where(a => a.Status is AppStatus.Attention or AppStatus.Changed || a.PausedCount > 0)
                         .OrderByDescending(a => a.Status == AppStatus.Attention)
                         .ThenByDescending(a => a.LastEventAt)
                         .Take(4))
            {
                AttentionItems.Add(app);
            }

            HasAttention = AttentionItems.Count > 0;
            AttentionSummary = stats.AttentionCount > 0
                ? string.Format(LocalizedStrings.Instance["Ui.AttentionCountFormat"], stats.AttentionCount)
                : stats.PausedCount > 0
                    ? string.Format(LocalizedStrings.Instance["Ui.PausedCountFormat"], stats.PausedCount)
                    : LocalizedStrings.Instance["Ui.AllCalm"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить обзор.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScanNowAsync() => await Main.ScanNowAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenHistoryAsync() => await Main.NavigateAsync(nameof(AppSection.History)).ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenChangedAsync() => await Main.OpenProgramsAsync("Changed").ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenAttentionAsync() => await Main.OpenProgramsAsync("Attention").ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenAppAsync(AppSummary? app)
    {
        if (app is not null)
        {
            await Main.OpenAppAsync(app.Id).ConfigureAwait(true);
        }
    }

}
