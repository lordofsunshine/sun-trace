using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;

namespace SunTrace.Infrastructure.Monitoring;

public sealed class TrackingService
{
    private readonly IScanCoordinator _coordinator;
    private readonly IHistoryStore _store;
    private readonly ILogger<TrackingService> _logger;

    public TrackingService(IScanCoordinator coordinator, IHistoryStore store, ILogger<TrackingService> logger)
    {
        _coordinator = coordinator;
        _store = store;
        _logger = logger;
    }

    public async Task<TrackingSession> StartAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var session = new TrackingSession
        {
            StartedAt = DateTimeOffset.Now,
            Status = TrackingSessionStatus.ScanningBaseline
        };
        await _store.CreateSessionAsync(session, cancellationToken).ConfigureAwait(false);

        try
        {
            var outcome = await _coordinator
                .ScanAsync(ScanReason.Baseline, progress, session.Id, null, cancellationToken)
                .ConfigureAwait(false);
            session.BaselineSnapshotId = outcome.SnapshotId;
            session.Status = TrackingSessionStatus.AwaitingInstall;
            await _store.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            session.Status = TrackingSessionStatus.Cancelled;
            session.CompletedAt = DateTimeOffset.Now;
            await _store.UpdateSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<TrackingResult> CompleteAsync(TrackingSession session, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        session.Status = TrackingSessionStatus.ScanningResult;
        await _store.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);

        ScanOutcome outcome;
        try
        {
            outcome = await _coordinator
                .ScanAsync(ScanReason.TrackingResult, progress, session.Id, session.BaselineSnapshotId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            session.Status = TrackingSessionStatus.AwaitingInstall;
            await _store.UpdateSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var newApps = outcome.Diff?.AppChanges.Where(c => c.Kind == ChangeKind.Added && c.After is not null)
            .Select(c => c.After!)
            .ToList() ?? new List<InstalledApp>();
        var relatedEvents = outcome.Events.Where(e => e.SessionId == session.Id).ToList();

        session.Status = TrackingSessionStatus.Completed;
        session.CompletedAt = DateTimeOffset.Now;
        session.NewAppCount = newApps.Count;
        session.EventCount = relatedEvents.Count;
        session.Summary = newApps.Count > 0
            ? $"Установлено программ: {newApps.Count}. Обнаружено изменений: {relatedEvents.Count}."
            : $"Новые программы не обнаружены. Обнаружено изменений: {relatedEvents.Count}.";
        await _store.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Отслеживание завершено. Новых программ: {Apps}, событий: {Events}.", newApps.Count, relatedEvents.Count);
        return new TrackingResult
        {
            Session = session,
            Outcome = outcome,
            NewApps = newApps
        };
    }

    public async Task CancelAsync(TrackingSession session, CancellationToken cancellationToken)
    {
        session.Status = TrackingSessionStatus.Cancelled;
        session.CompletedAt = DateTimeOffset.Now;
        await _store.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public Task<TrackingSession?> GetActiveSessionAsync(CancellationToken cancellationToken) =>
        _store.GetActiveSessionAsync(cancellationToken);
}

public sealed class TrackingResult
{
    public TrackingSession Session { get; init; } = new();

    public ScanOutcome Outcome { get; init; } = new();

    public IReadOnlyList<InstalledApp> NewApps { get; init; } = Array.Empty<InstalledApp>();
}
