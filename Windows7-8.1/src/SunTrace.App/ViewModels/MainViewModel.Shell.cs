using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void DismissToast() => IsToastVisible = false;

    public async Task SaveWindowStateAsync(double width, double height, double left, double top, bool maximized)
    {
        try
        {
            var clone = _settings.Current.Clone();
            clone.WindowWidth = Math.Max(720, width);
            clone.WindowHeight = Math.Max(560, height);
            clone.WindowLeft = left;
            clone.WindowTop = top;
            clone.WindowMaximized = maximized;
            await _settings.SaveAsync(clone).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить положение окна.");
        }
    }

    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        var token = ++_toastVersion;
        _ = HideToastLaterAsync(token);
    }

    private async Task HideToastLaterAsync(int token)
    {
        await Task.Delay(4600).ConfigureAwait(true);
        if (token == _toastVersion)
        {
            IsToastVisible = false;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            IsMonitoringEnabled = settings.MonitoringEnabled;
            UpdateMonitoringState();
        });

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            (PageTitle, PageSubtitle) = CurrentSection switch
            {
                AppSection.Overview => (LocalizedStrings.Instance.Get("Shell.Overview"), LocalizedStrings.Instance.Get("Shell.OverviewSubtitle")),
                AppSection.Programs => (LocalizedStrings.Instance.Get("Shell.Programs"), LocalizedStrings.Instance.Get("Shell.ProgramsSubtitle")),
                AppSection.History => (LocalizedStrings.Instance.Get("Shell.History"), LocalizedStrings.Instance.Get("Shell.HistorySubtitle")),
                AppSection.Paused => (LocalizedStrings.Instance.Get("Shell.Paused"), LocalizedStrings.Instance.Get("Shell.PausedSubtitle")),
                _ => (LocalizedStrings.Instance.Get("Shell.Settings"), LocalizedStrings.Instance.Get("Shell.SettingsSubtitle"))
            };
            UpdateMonitoringState();
        });

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitoring.ScanCompleted -= OnScanCompleted;
        _monitoring.StateChanged -= OnMonitoringStateChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        LocalizedStrings.Instance.LanguageChanged -= OnLanguageChanged;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        await Task.CompletedTask;
    }
}
