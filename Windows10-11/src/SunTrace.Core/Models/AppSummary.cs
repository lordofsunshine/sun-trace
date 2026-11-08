using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class AppSummary
{
    public InstalledApp App { get; init; } = new();

    public DateTimeOffset FirstSeen { get; init; }

    public DateTimeOffset LastSeen { get; init; }

    public DateTimeOffset? LastEventAt { get; init; }

    public DateTimeOffset? LastChangeAt { get; init; }

    public DateTimeOffset? LastUpdateAt { get; init; }

    public int ComponentCount { get; init; }

    public int PausedCount { get; init; }

    public int AttentionCount { get; init; }

    public int UnreadCount { get; init; }

    public int EventCount { get; init; }

    public int RecentChangeCount { get; init; }

    public AppStatus Status { get; init; }

    public bool HasStatus => Status != AppStatus.Unchanged;

    public string Id => App.Id;

    public string Name => App.Name;

    public string Publisher => App.DisplayPublisher;

    public string ComponentsLabel => ComponentCount switch
    {
        0 => "Компоненты не обнаружены",
        1 => "1 компонент",
        >= 2 and <= 4 => $"{ComponentCount} компонента",
        _ => $"{ComponentCount} компонентов"
    };

    public string PausedLabel => PausedCount switch
    {
        0 => string.Empty,
        1 => "1 отключён",
        >= 2 and <= 4 => $"{PausedCount} отключено",
        _ => $"{PausedCount} отключено"
    };
}

public sealed class DashboardStats
{
    public int AppCount { get; init; }

    public int EventCount { get; init; }

    public int EventsLast7Days { get; init; }

    public int UpdatedAppsLast30Days { get; init; }

    public int AttentionCount { get; init; }

    public int PausedCount { get; init; }

    public int ChangedAppCount { get; init; }

    public int UnreadCount { get; init; }

    public DateTimeOffset? LastScanAt { get; init; }

    public ScanReason? LastScanReason { get; init; }

    public bool LastScanComplete { get; init; }

    public int SnapshotCount { get; init; }

    public int StartupCount { get; init; }

    public int ServiceCount { get; init; }

    public int TaskCount { get; init; }
}

public sealed class DatabaseHealth
{
    public bool IsHealthy { get; init; }

    public string Message { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public int SchemaVersion { get; init; }

    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
}

public sealed class SearchResult
{
    public SearchResultKind Kind { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string TargetId { get; init; } = string.Empty;

    public string? AppId { get; init; }
}

public sealed class TrackingSession
{
    public long Id { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? CompletedAt { get; set; }

    public long? BaselineSnapshotId { get; set; }

    public string? InstallerPath { get; set; }

    public TrackingSessionStatus Status { get; set; } = TrackingSessionStatus.ScanningBaseline;

    public string? Summary { get; set; }

    public int NewAppCount { get; set; }

    public int EventCount { get; set; }
}

public sealed class PausedComponent
{
    public bool IsActive => Status is PauseStatus.Active or PauseStatus.Conflict or PauseStatus.Failed or PauseStatus.TargetMissing;

    public long Id { get; set; }

    public string ComponentId { get; set; } = string.Empty;

    public ComponentType ComponentType { get; set; }

    public string? AppId { get; set; }

    public string AppName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public PauseDescriptor Descriptor { get; set; } = new();

    public DateTimeOffset PausedAt { get; set; }

    public string? Reason { get; set; }

    public PauseStatus Status { get; set; } = PauseStatus.Active;

    public string? LastError { get; set; }

    public string ComponentTypeLabel => ComponentType switch
    {
        ComponentType.Startup => "Автозагрузка",
        ComponentType.ScheduledTask => "Задание планировщика",
        _ => "Компонент"
    };
}

public sealed class PauseResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public PausedComponent? Paused { get; init; }

    public bool RequiresElevation { get; init; }
}

public sealed class RestoreResult
{
    public RestoreOutcome Outcome { get; init; }

    public string Message { get; init; } = string.Empty;

    public PausedComponent? Item { get; init; }
}
