using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SunTrace.App.Services;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.App.Localization;
using SunTrace.Infrastructure.Monitoring;

namespace SunTrace.App.ViewModels;

public partial class TrackingDialogViewModel : ObservableObject
{
    private readonly TrackingService _tracking;
    private readonly IDialogService _dialogs;
    private readonly ILogger<TrackingDialogViewModel> _logger;
    private TrackingSession? _session;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntro))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntro))]
    private bool _isAwaitingInstall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntro))]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntro))]
    private bool _isFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntro))]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string _statusMessage = LocalizedStrings.Instance["Ui.TrackingInitialStatus"];

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _installerPath = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _newAppsLabel = LocalizedStrings.Instance["Ui.NoNewApps"];

    [ObservableProperty]
    private string _eventsLabel = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _resultTitle = LocalizedStrings.Instance["Ui.TrackingResultDone"];

    public TrackingDialogViewModel(
        TrackingService tracking,
        IDialogService dialogs,
        ILogger<TrackingDialogViewModel> logger)
    {
        _tracking = tracking;
        _dialogs = dialogs;
        _logger = logger;
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<InstalledApp> NewApps { get; } = new();

    public ObservableCollection<ChangeEvent> Changes { get; } = new();

    public bool IsIntro => !IsBusy && !IsAwaitingInstall && !IsCompleted && !IsFailed && !HasActiveSession;

    public bool HasInstaller => !string.IsNullOrWhiteSpace(InstallerPath);

    public string InstallerLabel => string.IsNullOrWhiteSpace(InstallerPath)
        ? LocalizedStrings.Instance["Ui.InstallerNotSelected"]
        : Path.GetFileName(InstallerPath);

    public async Task InitializeAsync()
    {
        try
        {
            var active = await _tracking.GetActiveSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (active is { Status: TrackingSessionStatus.AwaitingInstall })
            {
                _session = active;
                HasActiveSession = true;
                StatusMessage = string.Format(LocalizedStrings.Instance["Ui.SessionResumed"], active.StartedAt.ToLocalTime().ToString("d MMMM, HH:mm", LocalizedStrings.Instance.Culture));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось проверить активную сессию отслеживания.");
        }
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (_session is null)
        {
            return;
        }

        HasActiveSession = false;
        IsAwaitingInstall = true;
        StatusMessage = LocalizedStrings.Instance["Ui.SessionContinueStatus"];
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsFailed = false;
        Progress = 0;
        StatusMessage = LocalizedStrings.Instance["Ui.SavingInitial"];
        _cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ScanProgress>(value =>
            {
                Progress = value.Percentage;
                if (!string.IsNullOrWhiteSpace(value.Message))
                {
                    ProgressText = value.Message;
                }
            });

            _session = await _tracking.StartAsync(progress, _cancellation.Token).ConfigureAwait(true);
            IsAwaitingInstall = true;
            Progress = 100;
            StatusMessage = LocalizedStrings.Instance["Ui.InitialSavedStatus"];
        }
        catch (OperationCanceledException)
        {
            IsFailed = true;
            ErrorMessage = LocalizedStrings.Instance["Ui.TrackingCancelled"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось начать отслеживание установки.");
            IsFailed = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ChooseInstaller()
    {
        var path = _dialogs.PickFile(LocalizedStrings.Instance["Ui.PickInstallerTitle"], LocalizedStrings.Instance["Ui.InstallerFilter"]);
        if (!string.IsNullOrWhiteSpace(path))
        {
            InstallerPath = path;
            OnPropertyChanged(nameof(InstallerLabel));
            OnPropertyChanged(nameof(HasInstaller));
        }
    }

    [RelayCommand]
    private void RunInstaller()
    {
        if (string.IsNullOrWhiteSpace(InstallerPath) || !File.Exists(InstallerPath))
        {
            _dialogs.Inform(LocalizedStrings.Instance["Ui.InstallerNotFound"], LocalizedStrings.Instance["Ui.InstallerNotFoundMessage"]);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(InstallerPath) { UseShellExecute = true });
            StatusMessage = LocalizedStrings.Instance["Ui.InstallerStarted"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось запустить установщик.");
            _dialogs.Inform(LocalizedStrings.Instance["Ui.InstallerStartFailed"], ex.Message + "\n\n" + LocalizedStrings.Instance["Ui.InstallerStartFailedMessage"]);
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (IsBusy || _session is null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusMessage = LocalizedStrings.Instance["Ui.ComparingSystem"];
        _cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ScanProgress>(value =>
            {
                Progress = value.Percentage;
                if (!string.IsNullOrWhiteSpace(value.Message))
                {
                    ProgressText = value.Message;
                }
            });

            var result = await _tracking.CompleteAsync(_session, progress, _cancellation.Token).ConfigureAwait(true);
            IsAwaitingInstall = false;
            IsCompleted = true;
            Progress = 100;
            Summary = result.Session.Summary ?? LocalizedStrings.Instance["Ui.TrackingResultDone"];
            NewApps.Clear();
            foreach (var app in result.NewApps)
            {
                NewApps.Add(app);
            }

            Changes.Clear();
            foreach (var item in result.Outcome.Events)
            {
                Changes.Add(ChangeEventLocalizer.Localize(item));
            }

            HasResults = NewApps.Count > 0 || Changes.Count > 0;
            NewAppsLabel = NewApps.Count == 0
                ? LocalizedStrings.Instance["Ui.NoNewApps"]
                : string.Format(LocalizedStrings.Instance["Ui.NewAppsCount"], NewApps.Count);
            EventsLabel = Changes.Count == 0
                ? LocalizedStrings.Instance["Ui.NoChangesFound"]
                : string.Format(LocalizedStrings.Instance["Ui.RelatedEventsCount"], Changes.Count);
            ResultTitle = NewApps.Count > 0 ? LocalizedStrings.Instance["Ui.ProgramInstalled"] : LocalizedStrings.Instance["Ui.TrackingResultDone"];
            StatusMessage = LocalizedStrings.Instance["Ui.ResultSaved"];
        }
        catch (OperationCanceledException)
        {
            IsFailed = true;
            ErrorMessage = LocalizedStrings.Instance["Ui.CompareCancelled"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось завершить отслеживание.");
            IsFailed = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task DiscardSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        if (!_dialogs.Confirm(LocalizedStrings.Instance["Ui.AbortTrackingTitle"], LocalizedStrings.Instance["Ui.AbortTrackingMessage"], LocalizedStrings.Instance["Ui.AbortTrackingAction"], danger: true))
        {
            return;
        }

        try
        {
            await _tracking.CancelAsync(_session, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отменить сессию отслеживания.");
        }

        _session = null;
        HasActiveSession = false;
        IsAwaitingInstall = false;
        StatusMessage = LocalizedStrings.Instance["Ui.SessionCancelled"];
    }

    public void CancelImmediately()
    {
        _cancellation?.Cancel();
        if (_session is { Status: TrackingSessionStatus.AwaitingInstall })
        {
            return;
        }

        if (_session is { Status: TrackingSessionStatus.ScanningBaseline or TrackingSessionStatus.ScanningResult })
        {
            _ = _tracking.CancelAsync(_session, CancellationToken.None);
        }
    }
}
