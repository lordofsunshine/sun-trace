using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;

namespace SunTrace.App.ViewModels;

public partial class SettingsViewModel
{
    [RelayCommand]
    private void SetTheme(string? value)
    {
        if (int.TryParse(value, out var index) && index is >= 0 and <= 2)
        {
            ThemeIndex = index;
        }
    }

    [RelayCommand]
    private void SetLanguage(string? value)
    {
        if (int.TryParse(value, out var index) && index is >= 0 and <= 2)
        {
            LanguageIndex = index;
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(_paths.Root);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(_paths.LogsDirectory);

    [RelayCommand]
    private void OpenExportsFolder() => OpenFolder(_paths.ExportsDirectory);

    [RelayCommand]
    private void CopyDataPath() => CopyText(_paths.Root);

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            Main.ShowToast(LocalizedStrings.Instance["Ui.PathCopied"]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось скопировать путь.");
        }
    }

    private void UpdateStorageInfo()
    {
        try
        {
            var info = new FileInfo(_paths.DatabasePath);
            DatabaseSizeLabel = info.Exists ? FormatSize(info.Length) : LocalizedStrings.Instance["Ui.DbNotCreated"];
        }
        catch
        {
            DatabaseSizeLabel = "-";
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedOptions();
        OnPropertyChanged(nameof(PrivacyText));
        OnPropertyChanged(nameof(BackupCountLabel));
        OnPropertyChanged(nameof(VersionLabel));
    }

    private void UpdateLocalizedOptions()
    {
        var intervals = new[]
        {
            (5, "Settings.Interval5Minutes"),
            (15, "Settings.Interval15Minutes"),
            (30, "Settings.Interval30Minutes"),
            (60, "Settings.IntervalHour"),
            (120, "Settings.Interval2Hours")
        };

        if (ScanIntervalOptions.Count == 0)
        {
            ScanIntervalOptions = intervals
                .Select(item => new SettingOption(item.Item1, LocalizedStrings.Instance.Get(item.Item2)))
                .ToArray();
            OnPropertyChanged(nameof(ScanIntervalOptions));
        }
        else
        {
            foreach (var option in ScanIntervalOptions)
            {
                var key = intervals.First(item => item.Item1 == option.Value).Item2;
                option.Label = LocalizedStrings.Instance.Get(key);
            }
        }

        if (RetentionOptions.Count == 0)
        {
            RetentionOptions = new[] { 10, 20, 30, 40, 60, 90, 120 }
                .Select(value => new SettingOption(value, string.Format(LocalizedStrings.Instance.Culture, LocalizedStrings.Instance.Get("Settings.Snapshots"), value)))
                .ToArray();
            OnPropertyChanged(nameof(RetentionOptions));
        }
        else
        {
            foreach (var option in RetentionOptions)
            {
                option.Label = string.Format(LocalizedStrings.Instance.Culture, LocalizedStrings.Instance.Get("Settings.Snapshots"), option.Value);
            }
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть папку {Path}.", path);
            _dialogs.Inform(LocalizedStrings.Instance["Ui.OpenFolderFailed"], ex.Message);
        }
    }
}
