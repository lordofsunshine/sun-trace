using CommunityToolkit.Mvvm.Input;
using SunTrace.App.Localization;
using SunTrace.App.Services;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class HistoryViewModel
{
    public async Task ApplyAppFilterAsync(string appId, string appName)
    {
        SetAppFilter(appId, appName);
        SetSilently(() => SearchText = string.Empty);
        CancelScheduledRefresh();
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ClearAppFilterAsync() => ApplyFilterAsync(() => SetAppFilter(null, string.Empty));

    [RelayCommand]
    private Task ClearFiltersAsync() => ApplyFilterAsync(() =>
    {
        SetAppFilter(null, string.Empty);
        KindFilter = "All";
        TypeFilter = "All";
        ImportanceFilter = "All";
        Period = "All";
        SearchText = string.Empty;
    });

    [RelayCommand]
    private async Task OpenAppAsync(ChangeEvent? item)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.AppId))
        {
            await Main.OpenAppAsync(item.AppId).ConfigureAwait(true);
        }
    }

    partial void OnSearchTextChanged(string value) => ScheduleRefresh(_searchDebouncer, eventsOnly: false);

    partial void OnKindFilterChanged(string value) => ScheduleRefresh(_filterDebouncer, eventsOnly: true);

    partial void OnTypeFilterChanged(string value) => ScheduleRefresh(_filterDebouncer, eventsOnly: false);

    partial void OnImportanceFilterChanged(string value) => ScheduleRefresh(_filterDebouncer, eventsOnly: true);

    partial void OnPeriodChanged(string value) => ScheduleRefresh(_filterDebouncer, eventsOnly: true);

    private void SetAppFilter(string? appId, string appName)
    {
        _appFilterId = appId;
        _appFilterName = appName;
        HasAppFilter = appId is not null;
        AppFilterLabel = appId is null
            ? string.Empty
            : string.Format(LocalizedStrings.Instance["Ui.OnlyAppFormat"], appName);
    }

    private async Task ApplyFilterAsync(Action change, bool eventsOnly = false)
    {
        SetSilently(change);
        CancelScheduledRefresh();
        if (eventsOnly && IsRewindMode)
        {
            return;
        }

        await RefreshCurrentModeAsync().ConfigureAwait(true);
    }

    private void SetSilently(Action change)
    {
        _suspendRefresh = true;
        try
        {
            change();
        }
        finally
        {
            _suspendRefresh = false;
        }
    }

    private void ScheduleRefresh(Debouncer debouncer, bool eventsOnly)
    {
        if (_suspendRefresh || (eventsOnly && IsRewindMode))
        {
            return;
        }

        debouncer.Run(RefreshCurrentModeAsync);
    }

    private void CancelScheduledRefresh()
    {
        _searchDebouncer.Cancel();
        _filterDebouncer.Cancel();
    }

    private Task RefreshCurrentModeAsync() => IsRewindMode ? CompareAsync() : ReloadAsync();
}
