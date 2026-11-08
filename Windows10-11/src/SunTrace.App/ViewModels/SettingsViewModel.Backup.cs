using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public partial class SettingsViewModel
{
    [RelayCommand]
    public async Task CreateBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await SaveNowAsync().ConfigureAwait(true);
            var path = await _backup.CreateBackupAsync().ConfigureAwait(true);
            await RefreshBackupsAsync().ConfigureAwait(true);
            SelectedBackup = Backups.FirstOrDefault();
            _dialogs.Inform(LocalizedStrings.Instance["Ui.BackupCreated"], path, LocalizedStrings.Instance["Ui.BackupCreatedMessage"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось создать резервную копию.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.BackupFailed"], ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var path = SelectedBackup?.Path ?? _dialogs.PickFile(LocalizedStrings.Instance["Ui.PickBackupTitle"], LocalizedStrings.Instance["Ui.BackupFilter"]);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var validation = await _backup.ValidateAsync(path).ConfigureAwait(true);
            if (!validation.IsValid)
            {
                _dialogs.Inform(LocalizedStrings.Instance["Ui.BackupInvalid"], validation.Message);
                return;
            }

            var confirmed = _dialogs.Confirm(
                LocalizedStrings.Instance["Ui.RestoreHistoryTitle"],
                string.Format(LocalizedStrings.Instance["Ui.RestoreHistoryBody"], validation.SnapshotCount, validation.EventCount),
                LocalizedStrings.Instance["Ui.Restore"],
                danger: true);
            if (!confirmed)
            {
                return;
            }

            await _backup.RestoreAsync(path, _store).ConfigureAwait(true);
            await Main.RefreshShellCountsAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            _dialogs.Inform(LocalizedStrings.Instance["Ui.HistoryRestored"], LocalizedStrings.Instance["Ui.HistoryRestoredMessage"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить историю.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.RestoreFailed"], ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        if (!_dialogs.Confirm(
                LocalizedStrings.Instance["Ui.ClearHistoryTitle"],
                LocalizedStrings.Instance["Ui.ClearHistoryBody"],
                LocalizedStrings.Instance["Ui.ClearHistoryAction"],
                danger: true))
        {
            return;
        }

        try
        {
            await _store.ClearHistoryAsync().ConfigureAwait(true);
            await Main.RefreshShellCountsAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            _dialogs.Inform(LocalizedStrings.Instance["Ui.HistoryCleared"], LocalizedStrings.Instance["Ui.HistoryClearedMessage"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось очистить историю.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.ClearFailed"], ex.Message);
        }
    }

    [RelayCommand]
    public async Task CheckIntegrityAsync()
    {
        try
        {
            var health = await _store.CheckIntegrityAsync().ConfigureAwait(true);
            HealthLabel = health.IsHealthy
                ? string.Format(LocalizedStrings.Instance["Ui.DbHealthyFormat"], health.SchemaVersion, FormatSize(health.SizeBytes))
                : LocalizedStrings.Instance["Ui.DbProblemPrefix"] + health.Message;
            _dialogs.Inform(
                health.IsHealthy ? LocalizedStrings.Instance["Ui.CheckComplete"] : LocalizedStrings.Instance["Ui.ProblemFound"],
                health.Message,
                string.Format(LocalizedStrings.Instance["Ui.DbDetailsFormat"], FormatSize(health.SizeBytes), health.SchemaVersion));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось проверить базу данных.");
            HealthLabel = LocalizedStrings.Instance["Ui.CheckFailed"];
            _dialogs.Inform(LocalizedStrings.Instance["Ui.CheckFailed"], ex.Message);
        }
    }

    public async Task RefreshBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backups = await _backup.ListBackupsAsync(cancellationToken).ConfigureAwait(true);
            Backups.Clear();
            foreach (var item in backups)
            {
                Backups.Add(item);
            }

            HasBackups = Backups.Count > 0;
            LastBackupLabel = Backups.Count == 0
                ? LocalizedStrings.Instance["Ui.NoBackupsYet"]
                : string.Format(LocalizedStrings.Instance["Ui.LastBackupFormat"], Backups[0].CreatedAt.ToLocalTime().ToString("d MMMM, HH:mm", LocalizedStrings.Instance.Culture));
            OnPropertyChanged(nameof(BackupCountLabel));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать список резервных копий.");
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = LocalizedStrings.Instance.Culture.TwoLetterISOLanguageName == "ru"
            ? ["Б", "КБ", "МБ", "ГБ"]
            : ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
