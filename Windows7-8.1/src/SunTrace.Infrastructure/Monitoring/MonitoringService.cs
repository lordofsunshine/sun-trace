using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Windows;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class MonitoringScanEventArgs : EventArgs
{
    public ScanReason Reason { get; init; }

    public ScanOutcome Outcome { get; init; } = new();

    public int ImportantCount { get; init; }
}

public sealed class MonitoringService : IAsyncDisposable
{
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinimumScanGap = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(4);
    private const int ConstrainedIntervalFactor = 2;

    private readonly IScanCoordinator _coordinator;
    private readonly IHistoryStore _store;
    private readonly ISettingsService _settings;
    private readonly SystemChangeWatcher _watcher;
    private readonly ILogger<MonitoringService> _logger;
    private readonly SemaphoreSlim _changeSignal = new(0, 1);
    private readonly object _sync = new();

    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private bool _enabled = true;
    private bool _started;
    private DateTimeOffset? _nextScanAt;
    private DateTimeOffset? _lastScanAt;

    public MonitoringService(
        IScanCoordinator coordinator,
        IHistoryStore store,
        ISettingsService settings,
        SystemChangeWatcher watcher,
        ILogger<MonitoringService> logger)
    {
        _coordinator = coordinator;
        _store = store;
        _settings = settings;
        _watcher = watcher;
        _logger = logger;
        _watcher.Changed += OnSystemChanged;
    }

    public event EventHandler<MonitoringScanEventArgs>? ScanCompleted;

    public event EventHandler? StateChanged;

    public bool IsScanning => _coordinator.IsScanning;

    public bool IsEnabled => _enabled;

    public TimeSpan Interval => _interval;

    public DateTimeOffset? NextScanAt => _nextScanAt;

    public DateTimeOffset? LastScanAt => _lastScanAt;

    public string? LastError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        var settings = _settings.Current;
        _enabled = settings.MonitoringEnabled;
        _interval = TimeSpan.FromMinutes(settings.ScanIntervalMinutes);

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var latest = await _store.GetSnapshotsAsync(1, cancellationToken).ConfigureAwait(false);
        _lastScanAt = latest.FirstOrDefault()?.CapturedAt;
        _started = true;

        if (latest.Count == 0)
        {
            await RunScanAsync(ScanReason.Baseline, null, cancellationToken).ConfigureAwait(false);
        }

        UpdateWatcher();
        StartLoop();
    }

    public void ApplySettings(AppSettings settings)
    {
        lock (_sync)
        {
            _enabled = settings.MonitoringEnabled;
            _interval = TimeSpan.FromMinutes(settings.ScanIntervalMinutes);
        }

        UpdateWatcher();
        StartLoop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<ScanOutcome?> ScanNowAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        RunScanAsync(ScanReason.Manual, progress, cancellationToken);

    public Task<ScanOutcome?> ScanBaselineAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        RunScanAsync(ScanReason.Baseline, progress, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _watcher.Changed -= OnSystemChanged;
        _watcher.Dispose();

        Task? loop;
        lock (_sync)
        {
            _loopCancellation?.Cancel();
            loop = _loopTask;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCancellation?.Dispose();
        _changeSignal.Dispose();
    }

    private async Task<ScanOutcome?> RunScanAsync(ScanReason reason, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            var outcome = await _coordinator
                .ScanAsync(reason, progress, null, null, cancellationToken)
                .ConfigureAwait(false);

            _lastScanAt = outcome.Snapshot.CapturedAt;
            LastError = null;
            var important = outcome.Events.Count(e => e.Importance >= ImportanceLevel.Check)
                            + outcome.Events.Count(e => e.Kind == ChangeKind.Added && e.ComponentType == ComponentType.Application);
            ScanCompleted?.Invoke(this, new MonitoringScanEventArgs
            {
                Reason = reason,
                Outcome = outcome,
                ImportantCount = important
            });
            return outcome;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "Сканирование завершилось ошибкой.");
            return null;
        }
        finally
        {
            _nextScanAt = DateTimeOffset.Now + EffectiveInterval();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartLoop()
    {
        lock (_sync)
        {
            _loopCancellation?.Cancel();
            _loopCancellation?.Dispose();
            _loopCancellation = new CancellationTokenSource();
            var token = _loopCancellation.Token;
            _nextScanAt = DateTimeOffset.Now + EffectiveInterval();
            _loopTask = Task.Run(() => LoopAsync(token));
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = EffectiveInterval();
            _nextScanAt = DateTimeOffset.Now + interval;
            StateChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                if (await _changeSignal.WaitAsync(interval, cancellationToken).ConfigureAwait(false))
                {
                    await SettleAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_enabled)
            {
                continue;
            }

            await RunScanAsync(ScanReason.Monitoring, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(ChangeDebounce, cancellationToken).ConfigureAwait(false);
        while (_changeSignal.Wait(0))
        {
        }

        if (_lastScanAt is { } last)
        {
            var remaining = MinimumScanGap - (DateTimeOffset.Now - last);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan EffectiveInterval()
    {
        var interval = _interval;
        if (PowerState.IsConstrained())
        {
            interval = TimeSpan.FromTicks(interval.Ticks * ConstrainedIntervalFactor);
        }

        return interval > MaximumInterval ? MaximumInterval : interval;
    }

    private void UpdateWatcher()
    {
        if (_enabled)
        {
            _watcher.Start();
        }
        else
        {
            _watcher.Stop();
        }
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            _changeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
