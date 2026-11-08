using SunTrace.Core.Enums;
using SunTrace.Core.Models;

namespace SunTrace.Core.Interfaces;

public interface ISystemScanner
{
    Task<SystemSnapshot> ScanAsync(ScanReason reason, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

public interface IScanCoordinator
{
    bool IsScanning { get; }

    Task<ScanOutcome> ScanAsync(ScanReason reason, IProgress<ScanProgress>? progress, long? sessionId, long? compareSnapshotId, CancellationToken cancellationToken);

    Task<SystemSnapshot> ScanWithoutHistoryAsync(ScanReason reason, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

public interface IHistoryStore : IAsyncDisposable
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<long> SaveSnapshotAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<SystemSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken = default);

    Task<SnapshotStamp?> GetLatestSnapshotStampAsync(CancellationToken cancellationToken = default);

    Task<SystemSnapshot?> GetSnapshotAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SnapshotSummary>> GetSnapshotsAsync(int limit = 120, CancellationToken cancellationToken = default);

    Task<int> AppendEventsAsync(IEnumerable<ChangeEvent> events, TimeSpan deduplicationWindow, CancellationToken cancellationToken = default);

    Task<EventPage> QueryEventsAsync(EventQuery query, CancellationToken cancellationToken = default);

    Task<ChangeEvent?> GetEventAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChangeEvent>> GetAppEventsAsync(string appId, int offset, int limit, CancellationToken cancellationToken = default);

    Task<int> GetAppEventCountAsync(string appId, CancellationToken cancellationToken = default);

    Task MarkAppEventsReadAsync(string appId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppSummary>> GetAppsAsync(CancellationToken cancellationToken = default);

    Task<AppSummary?> GetAppAsync(string appId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComponentSnapshot>> GetAppComponentsAsync(string appId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComponentSnapshot>> GetComponentsAsync(ComponentType? type, string? search, int limit, CancellationToken cancellationToken = default);

    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default);

    Task PruneSnapshotsAsync(int keep, CancellationToken cancellationToken = default);

    Task<int> PruneEventsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    Task<long> AddPausedAsync(PausedComponent item, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PausedComponent>> GetPausedAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task UpdatePausedAsync(PausedComponent item, CancellationToken cancellationToken = default);

    Task RemovePausedAsync(long id, CancellationToken cancellationToken = default);

    Task<long> CreateSessionAsync(TrackingSession session, CancellationToken cancellationToken = default);

    Task UpdateSessionAsync(TrackingSession session, CancellationToken cancellationToken = default);

    Task<TrackingSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(string term, int limit = 24, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetKnownAppNamesAsync(CancellationToken cancellationToken = default);

    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    Task<DatabaseHealth> CheckIntegrityAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    AppSettings Current { get; }

    string SettingsPath { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IComponentPauseService
{
    Task<PauseResult> PauseAsync(ComponentSnapshot component, string? appId, string? appName, string? reason, CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreAsync(PausedComponent item, CancellationToken cancellationToken = default);

    Task<RestoreResult> VerifyAsync(PausedComponent item, CancellationToken cancellationToken = default);

    Task<RestoreResult> KeepCurrentStateAsync(PausedComponent item, CancellationToken cancellationToken = default);

    Task ForgetAsync(PausedComponent item, CancellationToken cancellationToken = default);
}

public interface IAppIconProvider
{
    Task<byte[]?> GetPngAsync(string? executablePath, int size, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    void Show(string title, string message, NotificationSeverity severity);
}

public interface IReportExporter
{
    Task<string> ExportHtmlAsync(string appName, IReadOnlyList<ChangeEvent> events, string? publisher, CancellationToken cancellationToken = default);

    Task<string> ExportJsonAsync(string appName, IReadOnlyList<ChangeEvent> events, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<BackupValidation> ValidateAsync(string path, CancellationToken cancellationToken = default);

    Task RestoreAsync(string path, IHistoryStore store, CancellationToken cancellationToken = default);
}

public sealed class BackupInfo
{
    public string Path { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public long SizeBytes { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed class BackupValidation
{
    public bool IsValid { get; init; }

    public string Message { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public int SnapshotCount { get; init; }

    public int EventCount { get; init; }

    public long SizeBytes { get; init; }
}
