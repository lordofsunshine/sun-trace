using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Services;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public partial class PausedViewModel : ObservableObject, IPageViewModel
{
    private readonly IHistoryStore _store;
    private readonly IComponentPauseService _pause;
    private readonly IDialogService _dialogs;
    private readonly ILogger<PausedViewModel> _logger;
    private List<PausedComponent> _all = new();

    [ObservableProperty]
    private string _filter = "Active";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _historyCount;

    [ObservableProperty]
    private string _emptyTitle = LocalizedStrings.Instance["Ui.NoPaused"];

    [ObservableProperty]
    private string _emptyMessage = LocalizedStrings.Instance["Ui.NoPausedMessage"];

    public PausedViewModel(MainViewModel main, IHistoryStore store, IComponentPauseService pause, IDialogService dialogs, ILogger<PausedViewModel> logger)
    {
        Main = main;
        _store = store;
        _pause = pause;
        _dialogs = dialogs;
        _logger = logger;
        LocalizedStrings.Instance.LanguageChanged += OnLanguageChanged;
    }

    public MainViewModel Main { get; }

    public string ActiveLabel => string.Format(LocalizedStrings.Instance["Ui.ActiveTabFormat"], ActiveCount);

    public string HistoryLabel => string.Format(LocalizedStrings.Instance["Ui.HistoryTabFormat"], HistoryCount);

    public ObservableCollection<PausedComponent> Items { get; } = new();

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(ActiveLabel));

    partial void OnHistoryCountChanged(int value) => OnPropertyChanged(nameof(HistoryLabel));

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _all = (await _store.GetPausedAsync(false, cancellationToken).ConfigureAwait(true)).ToList();
            ActiveCount = _all.Count(p => p.IsActive);
            HistoryCount = _all.Count - ActiveCount;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить отключённые компоненты.");
            HasItems = false;
            EmptyTitle = LocalizedStrings.Instance["Ui.PausedLoadFailed"];
            EmptyMessage = LocalizedStrings.Instance["Ui.PausedLoadMessage"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetFilterAsync(string? value)
    {
        Filter = value is "History" or "All" ? value : "Active";
        ApplyFilter();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RestoreAsync(PausedComponent? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            LocalizedStrings.Instance["Ui.RestoreTitle"],
            string.Format(LocalizedStrings.Instance["Ui.RestoreBody"], item.DisplayName),
            LocalizedStrings.Instance["Ui.Restore"]);
        if (!confirmed)
        {
            return;
        }

        try
        {
            var result = await _pause.RestoreAsync(item).ConfigureAwait(true);
            if (result.Outcome == RestoreOutcome.Conflict)
            {
                var choice = _dialogs.ResolveRestoreConflict(item, result.Message);
                if (choice == RestoreConflictChoice.KeepCurrent)
                {
                    await _pause.KeepCurrentStateAsync(item).ConfigureAwait(true);
                    await RefreshAsync().ConfigureAwait(true);
                    await Main.RefreshShellCountsAsync().ConfigureAwait(true);
                    return;
                }

                if (choice != RestoreConflictChoice.Retry)
                {
                    return;
                }

                result = await _pause.RestoreAsync(item).ConfigureAwait(true);
            }

            var title = result.Outcome switch
            {
                RestoreOutcome.Restored => LocalizedStrings.Instance["Ui.RestoredTitle"],
                RestoreOutcome.Missing => LocalizedStrings.Instance["Ui.NotFoundTitle"],
                RestoreOutcome.RequiresElevation => LocalizedStrings.Instance["Ui.ElevationTitle"],
                _ => LocalizedStrings.Instance["Ui.RestoreFailed"]
            };
            _dialogs.Inform(title, result.Message);
            await RefreshAsync().ConfigureAwait(true);
            await Main.RefreshShellCountsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка восстановления компонента.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.RestoreFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task VerifyAsync(PausedComponent? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var result = await _pause.VerifyAsync(item).ConfigureAwait(true);
            var title = result.Outcome switch
            {
                RestoreOutcome.Restored => LocalizedStrings.Instance["Ui.RestoreChecked"],
                RestoreOutcome.Conflict => LocalizedStrings.Instance["Ui.VerifyConflict"],
                RestoreOutcome.Missing => LocalizedStrings.Instance["Ui.NotFoundTitle"],
                _ => LocalizedStrings.Instance["Ui.VerifyComplete"]
            };
            _dialogs.Inform(title, result.Message);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка проверки компонента.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.VerifyFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task ForgetAsync(PausedComponent? item)
    {
        if (item is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
                LocalizedStrings.Instance["Ui.ForgetTitle"],
                string.Format(LocalizedStrings.Instance["Ui.ForgetBody"], item.DisplayName),
                LocalizedStrings.Instance["Ui.ForgetAction"],
                danger: true))
        {
            return;
        }

        try
        {
            await _pause.ForgetAsync(item).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            await Main.RefreshShellCountsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось удалить запись восстановления.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.ForgetFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenAppAsync(PausedComponent? item)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.AppId))
        {
            await Main.OpenAppAsync(item.AppId).ConfigureAwait(true);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<PausedComponent> filtered = Filter switch
        {
            "History" => _all.Where(p => !p.IsActive),
            "All" => _all,
            _ => _all.Where(p => p.IsActive)
        };

        Items.Clear();
        foreach (var item in filtered.OrderByDescending(p => p.PausedAt))
        {
            Items.Add(item);
        }

        HasItems = Items.Count > 0;
        if (!HasItems)
        {
            EmptyTitle = Filter == "History" ? LocalizedStrings.Instance["Ui.RestoreHistoryEmptyTitle"] : LocalizedStrings.Instance["Ui.NoPaused"];
            EmptyMessage = Filter == "History"
                ? LocalizedStrings.Instance["Ui.HistoryEmptyMessage"]
                : LocalizedStrings.Instance["Ui.NoPausedMessage"];
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ActiveLabel));
        OnPropertyChanged(nameof(HistoryLabel));
        ApplyFilter();
    }
}
