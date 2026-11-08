using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.Core.Enums;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Monitoring;

namespace SunTrace.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    public async Task ScanNowAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanPercent = 0;
        ScanPercentText = string.Empty;
        ScanStatus = LocalizedStrings.Instance["Ui.Scanning"];
        try
        {
            var progress = new Progress<ScanProgress>(value =>
            {
                ScanPercent = value.Percentage;
                ScanPercentText = value.IsFinished ? string.Empty : $"{value.Percentage:0}%";
                if (!string.IsNullOrWhiteSpace(value.Message))
                {
                    ScanStatus = value.Message;
                }
            });

            var outcome = await _monitoring.ScanNowAsync(progress, CancellationToken.None).ConfigureAwait(true);
            if (outcome is null)
            {
                ScanStatus = LocalizedStrings.Instance["Ui.ScanFailed"];
                ShowToast(LocalizedStrings.Instance["Ui.ScanIncomplete"]);
                return;
            }

            ScanStatus = outcome.IsFirstScan
                ? LocalizedStrings.Instance["Ui.InitialStateSaved"]
                : string.Format(LocalizedStrings.Instance["Ui.CheckFinishedFormat"], outcome.Snapshot.Apps.Count);

            if (outcome.IsFirstScan)
            {
                ShowToast(outcome.FirstScanMessage ?? LocalizedStrings.Instance["Ui.InitialStateSaved"]);
            }
            else if (outcome.Events.Count > 0)
            {
                ShowToast(string.Format(LocalizedStrings.Instance["Ui.ChangesFoundFormat"], outcome.Events.Count));
            }
            else
            {
                ShowToast(LocalizedStrings.Instance["Ui.NoChangesFound"]);
            }

            await RefreshAfterScanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка ручного сканирования.");
            ScanStatus = LocalizedStrings.Instance["Ui.ScanError"];
            ShowToast(LocalizedStrings.Instance["Ui.ScanErrorMessage"]);
        }
        finally
        {
            IsScanning = false;
            UpdateMonitoringState();
        }
    }

    [RelayCommand]
    public async Task StartTrackingAsync()
    {
        _dialogs.ShowTracker();
        await RefreshAfterScanAsync().ConfigureAwait(true);
    }

    public async Task RefreshShellCountsAsync()
    {
        try
        {
            var stats = await _store.GetDashboardStatsAsync().ConfigureAwait(true);
            PausedCount = stats.PausedCount;
            AttentionCount = stats.AttentionCount;
            SectionBadge = stats.AttentionCount > 0 ? stats.AttentionCount.ToString() : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось обновить счётчики.");
        }
    }

    private async Task RefreshAfterScanAsync()
    {
        try
        {
            await RefreshCurrentAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось обновить текущий раздел после сканирования.");
        }
    }

    private void OnScanCompleted(object? sender, MonitoringScanEventArgs e)
    {
        _ = _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (e.Reason == ScanReason.Manual)
                {
                    return;
                }

                IsScanning = false;
                UpdateMonitoringState();
                var count = e.Outcome.Events.Count;
                ScanStatus = e.Outcome.IsFirstScan
                    ? LocalizedStrings.Instance["Ui.InitialStateSaved"]
                    : string.Format(LocalizedStrings.Instance["Ui.BackgroundCheckFormat"], count);

                if (e.Outcome.IsFirstScan)
                {
                    ShowToast(e.Outcome.FirstScanMessage ?? LocalizedStrings.Instance["Ui.InitialStateSaved"]);
                }
                else if (count > 0 && e.Reason != ScanReason.Manual)
                {
                    var primary = e.Outcome.Events
                        .Where(item => !string.IsNullOrWhiteSpace(item.AppId))
                        .OrderByDescending(item => item.Importance)
                        .FirstOrDefault();
                    var message = primary?.AppName is { Length: > 0 } name
                        ? string.Format(LocalizedStrings.Instance["Ui.AppChangesToastFormat"], name)
                        : string.Format(LocalizedStrings.Instance["Ui.ChangesToast"], count);
                    ShowToast(message);
                    NotificationRequested?.Invoke(this, new NotificationEventArgs
                    {
                        Title = primary is null ? "SunTrace" : primary.AppName,
                        Message = message,
                        AppId = primary?.AppId
                    });
                }

                await RefreshAfterScanAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки результата сканирования.");
            }
        });
    }

    private void OnMonitoringStateChanged(object? sender, EventArgs e) =>
        _ = _dispatcher.InvokeAsync(UpdateMonitoringState);

    private void UpdateMonitoringState()
    {
        IsMonitoringEnabled = _monitoring.IsEnabled;
        IsScanning = _monitoring.IsScanning;
        MonitoringLabel = _monitoring.IsEnabled ? LocalizedStrings.Instance["Ui.MonitoringOn"] : LocalizedStrings.Instance["Ui.MonitoringPaused"];
        NextScanLabel = !_monitoring.IsEnabled
            ? LocalizedStrings.Instance["Ui.AutoChecksOff"]
            : _monitoring.IsScanning
                ? LocalizedStrings.Instance["Ui.CheckingSystem"]
                : _monitoring.NextScanAt is { } next
                    ? string.Format(LocalizedStrings.Instance["Ui.NextCheckFormat"], next.ToLocalTime().ToString("HH:mm"))
                    : LocalizedStrings.Instance["Ui.WaitingFirstCheck"];

        if (!string.IsNullOrWhiteSpace(_monitoring.LastError))
        {
            ScanStatus = _monitoring.LastError!;
        }
    }
}
