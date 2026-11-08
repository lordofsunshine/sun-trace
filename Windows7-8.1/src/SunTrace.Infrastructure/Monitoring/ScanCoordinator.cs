using Microsoft.Extensions.Logging;
using SunTrace.Core.Detection;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class ScanCoordinator : IScanCoordinator
{
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromHours(12);
    private static readonly TimeSpan EventRetention = TimeSpan.FromDays(365);

    private readonly ISystemScanner _scanner;
    private readonly IHistoryStore _store;
    private readonly ISettingsService _settings;
    private readonly ILogger<ScanCoordinator> _logger;
    private readonly SnapshotComparer _comparer = new();
    private readonly ChangeEventFactory _eventFactory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SystemSnapshot? _cachedLatest;
    private SnapshotStamp? _cachedStamp;
    private DateTime _lastEventPruneDate = DateTime.MinValue;

    public ScanCoordinator(
        ISystemScanner scanner,
        IHistoryStore store,
        ISettingsService settings,
        ILogger<ScanCoordinator> logger)
    {
        _scanner = scanner;
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    public bool IsScanning => _gate.CurrentCount == 0;

    public async Task<ScanOutcome> ScanAsync(
        ScanReason reason,
        IProgress<ScanProgress>? progress,
        long? sessionId,
        long? compareSnapshotId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var previous = await LoadPreviousAsync(compareSnapshotId, cancellationToken).ConfigureAwait(false);
            if (compareSnapshotId is not null && previous is null)
            {
                throw new InvalidOperationException("Исходный снимок для сравнения недоступен. Начните отслеживание заново.");
            }

            var snapshot = await _scanner.ScanAsync(reason, progress, cancellationToken).ConfigureAwait(false);
            snapshot.SessionId = sessionId;
            var snapshotId = await _store.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
            snapshot.Id = snapshotId;

            var isEmptyBaseline = previous is null || (previous.Apps.Count == 0 && previous.Components.Count == 0);
            if (!isEmptyBaseline && previous!.Id == snapshotId)
            {
                return new ScanOutcome
                {
                    Snapshot = snapshot,
                    SnapshotId = snapshotId,
                    Diff = new SnapshotDiff(),
                    Events = Array.Empty<ChangeEvent>()
                };
            }

            await CacheLatestAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await _store.PruneSnapshotsAsync(_settings.Current.SnapshotRetention, cancellationToken).ConfigureAwait(false);
            await PruneEventsIfDueAsync(cancellationToken).ConfigureAwait(false);

            if (isEmptyBaseline)
            {
                _logger.LogInformation("Создан исходный снимок системы. Программ: {Apps}.", snapshot.Apps.Count);
                return new ScanOutcome
                {
                    Snapshot = snapshot,
                    SnapshotId = snapshotId,
                    IsFirstScan = true,
                    FirstScanMessage = $"Исходное состояние сохранено: найдено {snapshot.Apps.Count} программ и {snapshot.Components.Count} компонентов."
                };
            }

            var diff = _comparer.Compare(previous!, snapshot);
            var events = diff.HasChanges
                ? _eventFactory.Build(diff, previous!, snapshot, snapshotId, snapshot.CapturedAt, sessionId)
                : new List<ChangeEvent>();
            var inserted = events.Count == 0
                ? 0
                : await _store.AppendEventsAsync(
                    events,
                    reason == ScanReason.TrackingResult ? TimeSpan.Zero : DeduplicationWindow,
                    cancellationToken).ConfigureAwait(false);

            if (inserted > 0)
            {
                _logger.LogInformation("Обнаружено изменений: {Count}.", inserted);
            }

            return new ScanOutcome
            {
                Snapshot = snapshot,
                SnapshotId = snapshotId,
                Diff = diff,
                Events = events,
                IsFirstScan = false
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SystemSnapshot> ScanWithoutHistoryAsync(
        ScanReason reason,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _scanner.ScanAsync(reason, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SystemSnapshot?> LoadPreviousAsync(long? compareSnapshotId, CancellationToken cancellationToken)
    {
        if (compareSnapshotId is not null)
        {
            return await _store.GetSnapshotAsync(compareSnapshotId.Value, cancellationToken).ConfigureAwait(false);
        }

        var stamp = await _store.GetLatestSnapshotStampAsync(cancellationToken).ConfigureAwait(false);
        if (stamp is null)
        {
            _cachedLatest = null;
            _cachedStamp = null;
            return null;
        }

        if (_cachedLatest is not null && _cachedStamp == stamp)
        {
            return _cachedLatest;
        }

        var latest = await _store.GetLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (latest is not null && latest.Id == stamp.Value.Id)
        {
            _cachedLatest = latest;
            _cachedStamp = stamp;
        }

        return latest;
    }

    private async Task CacheLatestAsync(SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        var stamp = await _store.GetLatestSnapshotStampAsync(cancellationToken).ConfigureAwait(false);
        if (stamp is { } value && value.Id == snapshot.Id)
        {
            _cachedLatest = snapshot;
            _cachedStamp = value;
            return;
        }

        _cachedLatest = null;
        _cachedStamp = null;
    }

    private async Task PruneEventsIfDueAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        if (_lastEventPruneDate == today)
        {
            return;
        }

        await _store.PruneEventsAsync(DateTimeOffset.Now - EventRetention, cancellationToken).ConfigureAwait(false);
        _lastEventPruneDate = today;
    }
}
