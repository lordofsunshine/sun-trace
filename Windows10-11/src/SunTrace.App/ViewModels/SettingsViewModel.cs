using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SunTrace.App.Localization;
using SunTrace.App.Services;
using SunTrace.App.Theme;
using SunTrace.Core.Interfaces;
using SunTrace.Infrastructure;
using SunTrace.Infrastructure.Monitoring;
using SunTrace.Infrastructure.Services;

namespace SunTrace.App.ViewModels;

public sealed partial class SettingOption : ObservableObject
{
    public SettingOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    [ObservableProperty]
    private string _label;

    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject, IPageViewModel
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;
    private readonly IBackupService _backup;
    private readonly IHistoryStore _store;
    private readonly AutostartService _autostart;
    private readonly MonitoringService _monitoring;
    private readonly IDialogService _dialogs;
    private readonly DataPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;
    private CancellationTokenSource? _saveCancellation;
    private bool _loading = true;
    private bool _monitoringDirty;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private bool _monitoringEnabled;

    [ObservableProperty]
    private int _scanIntervalMinutes = 15;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private int _snapshotRetention = 40;

    [ObservableProperty]
    private string _saveState = LocalizedStrings.Instance["Ui.SaveAuto"];

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _databaseSizeLabel = "-";

    [ObservableProperty]
    private string _healthLabel = LocalizedStrings.Instance["Ui.CheckNotRun"];

    [ObservableProperty]
    private string _lastBackupLabel = LocalizedStrings.Instance["Ui.NoBackupsYet"];

    [ObservableProperty]
    private bool _hasBackups;

    [ObservableProperty]
    private BackupInfo? _selectedBackup;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        MainViewModel main,
        ISettingsService settings,
        ThemeManager theme,
        IBackupService backup,
        IHistoryStore store,
        AutostartService autostart,
        MonitoringService monitoring,
        IDialogService dialogs,
        DataPaths paths,
        ILogger<SettingsViewModel> logger)
    {
        Main = main;
        _settings = settings;
        _theme = theme;
        _backup = backup;
        _store = store;
        _autostart = autostart;
        _monitoring = monitoring;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        DatabasePath = paths.DatabasePath;
        LocalizedStrings.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedOptions();
    }

    public MainViewModel Main { get; }

    public IReadOnlyList<SettingOption> ScanIntervalOptions { get; private set; } = [];
    public IReadOnlyList<SettingOption> RetentionOptions { get; private set; } = [];
    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public string VersionLabel => "SunTrace " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public string DataFolder => _paths.Root;
    public string LogsFolder => _paths.LogsDirectory;
    public string ExportsFolder => _paths.ExportsDirectory;
    public string SettingsPath => _paths.SettingsPath;
    public string PrivacyText => LocalizedStrings.Instance.Get("Settings.Privacy");

    public string BackupCountLabel => Backups.Count == 0
        ? LocalizedStrings.Instance["Ui.NoCopies"]
        : string.Format(LocalizedStrings.Instance["Ui.BackupsCountFormat"], Backups.Count);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;
        try
        {
            var settings = _settings.Current;
            ThemeIndex = (int)settings.Theme;
            LanguageIndex = (int)settings.Language;
            MonitoringEnabled = settings.MonitoringEnabled;
            ScanIntervalMinutes = settings.ScanIntervalMinutes;
            NotificationsEnabled = settings.NotificationsEnabled;
            StartWithWindows = settings.StartWithWindows;
            SnapshotRetention = settings.SnapshotRetention;
            DatabasePath = _paths.DatabasePath;
            UpdateStorageInfo();
            await RefreshBackupsAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }
    }
}
