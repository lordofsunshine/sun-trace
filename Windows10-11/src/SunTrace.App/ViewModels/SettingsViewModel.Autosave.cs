using System.Reflection;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.Core.Enums;

namespace SunTrace.App.ViewModels;

public partial class SettingsViewModel
{
    partial void OnThemeIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _theme.Apply((ThemeMode)value);
        QueueSave();
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        LocalizedStrings.Instance.Apply((AppLanguage)value);
        QueueSave();
    }

    partial void OnMonitoringEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _monitoringDirty = true;
        QueueSave();
    }

    partial void OnScanIntervalMinutesChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _monitoringDirty = true;
        QueueSave();
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (!_loading)
        {
            QueueSave();
        }
    }

    partial void OnSnapshotRetentionChanged(int value)
    {
        if (!_loading)
        {
            QueueSave();
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (!_autostart.SetEnabled(value))
        {
            _dialogs.Inform(LocalizedStrings.Instance["Ui.AutoStartFailed"], LocalizedStrings.Instance["Ui.AutoStartFailedMessage"]);
            _loading = true;
            StartWithWindows = !value;
            _loading = false;
            return;
        }

        QueueSave();
    }

    private void QueueSave()
    {
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_saveCancellation.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(220, cancellationToken).ConfigureAwait(true);
            await SaveNowAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveNowAsync()
    {
        var settings = _settings.Current.Clone();
        settings.Theme = (ThemeMode)ThemeIndex;
        settings.Language = (AppLanguage)LanguageIndex;
        settings.MonitoringEnabled = MonitoringEnabled;
        settings.ScanIntervalMinutes = ScanIntervalMinutes;
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.StartWithWindows = StartWithWindows;
        settings.SnapshotRetention = SnapshotRetention;

        try
        {
            await _settings.SaveAsync(settings).ConfigureAwait(true);
            SaveState = string.Format(LocalizedStrings.Instance["Ui.SavedAtFormat"], DateTime.Now.ToString("HH:mm", LocalizedStrings.Instance.Culture));
            if (_monitoringDirty)
            {
                _monitoring.ApplySettings(settings);
                _monitoringDirty = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить настройки.");
            SaveState = LocalizedStrings.Instance["Ui.SaveFailed"];
        }
    }
}
