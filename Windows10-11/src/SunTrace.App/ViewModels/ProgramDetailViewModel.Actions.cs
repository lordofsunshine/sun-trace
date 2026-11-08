using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.App.Services;
using SunTrace.Core.Models;

namespace SunTrace.App.ViewModels;

public partial class ProgramDetailViewModel
{
    [RelayCommand]
    private async Task BackAsync() => await Main.NavigateAsync(nameof(AppSection.Programs)).ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenHistoryAsync() => await Main.ShowHistoryForAppAsync(AppId, Title).ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenPausedAsync() => await Main.NavigateAsync(nameof(AppSection.Paused)).ConfigureAwait(true);

    [RelayCommand]
    private void OpenLocation()
    {
        var path = ResolveLocation();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _dialogs.Inform(LocalizedStrings.Instance["Ui.FolderMissing"], LocalizedStrings.Instance["Ui.FolderMissingMessage"]);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(Path.GetFullPath(path));
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _dialogs.Inform(LocalizedStrings.Instance["Ui.OpenFolderFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private void CopyUninstall() => CopyText(UninstallCommand);

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            Main.ShowToast(LocalizedStrings.Instance["Ui.Copied"]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось скопировать текст.");
        }
    }

    [RelayCommand]
    private async Task ToggleExclusionAsync()
    {
        if (App is null)
        {
            return;
        }

        try
        {
            var settings = _settings.Current.Clone();
            var existing = settings.ExcludedPrograms.FirstOrDefault(item =>
                string.Equals(item.AppId, AppId, StringComparison.OrdinalIgnoreCase));
            var excluded = existing is null;
            if (excluded)
            {
                settings.ExcludedPrograms.Add(new ExcludedProgram { AppId = AppId, Name = App.Name });
            }
            else
            {
                settings.ExcludedPrograms.Remove(existing!);
            }

            await _settings.SaveAsync(settings).ConfigureAwait(true);
            IsExcluded = excluded;
            Main.ShowToast(LocalizedStrings.Instance[excluded ? "Ui.ExcludedToast" : "Ui.IncludedToast"]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось изменить исключения.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.SaveFailed"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task PauseAsync(ComponentItemViewModel? item)
    {
        if (item?.Component.Pause is null)
        {
            _dialogs.Inform(LocalizedStrings.Instance["Ui.PauseUnavailable"], LocalizedStrings.Instance["Ui.PauseUnavailableMessage"]);
            return;
        }

        var confirmed = _dialogs.Confirm(
            LocalizedStrings.Instance["Ui.PauseConfirmTitle"],
            string.Format(LocalizedStrings.Instance["Ui.PauseConfirmMessage"], item.Name),
            LocalizedStrings.Instance["Ui.Disable"],
            detail: LocalizedStrings.Instance["Ui.PauseDetail"]);
        if (!confirmed)
        {
            return;
        }

        try
        {
            var result = await _pause.PauseAsync(item.Component, AppId, Title, LocalizedStrings.Instance["Ui.PauseReason"]).ConfigureAwait(true);
            if (result.Success)
            {
                item.IsPaused = true;
                _dialogs.Inform(LocalizedStrings.Instance["Ui.PausedTitle"], result.Message);
                await RefreshAsync().ConfigureAwait(true);
                await Main.RefreshShellCountsAsync().ConfigureAwait(true);
            }
            else
            {
                _dialogs.Inform(LocalizedStrings.Instance[result.RequiresElevation ? "Ui.ElevationTitle" : "Ui.PauseFailedTitle"], result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка временного отключения компонента.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.PauseFailedTitle"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportHtmlAsync() => await ExportAsync(html: true).ConfigureAwait(true);

    [RelayCommand]
    private async Task ExportJsonAsync() => await ExportAsync(html: false).ConfigureAwait(true);

    private async Task ExportAsync(bool html)
    {
        try
        {
            var events = new List<ChangeEvent>();
            while (true)
            {
                var page = await _store.GetAppEventsAsync(AppId, events.Count, 500).ConfigureAwait(true);
                events.AddRange(page);
                if (page.Count < 500)
                {
                    break;
                }
            }

            var localizedEvents = events.Select(ChangeEventLocalizer.Localize).ToList();
            var path = html
                ? await _exporter.ExportHtmlAsync(Title, localizedEvents, Publisher).ConfigureAwait(true)
                : await _exporter.ExportJsonAsync(Title, localizedEvents).ConfigureAwait(true);
            _dialogs.InformWithAction(
                LocalizedStrings.Instance["Ui.ReportSaved"],
                path,
                LocalizedStrings.Instance["Ui.ReportSavedMessage"],
                LocalizedStrings.Instance["Ui.GoToFile"],
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось экспортировать отчёт.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.ReportFailed"], ex.Message);
        }
    }

    private string? ResolveLocation()
    {
        foreach (var candidate in new[] { AppInfo?.MainExecutablePath, InstallLocation, AppInfo?.InstallLocation })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var path = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (File.Exists(path))
            {
                return Path.GetDirectoryName(path);
            }

            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
