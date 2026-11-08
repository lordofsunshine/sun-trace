namespace SunTrace.Core.Enums;

public enum ComponentType
{
    Application = 0,
    Startup = 1,
    Service = 2,
    ScheduledTask = 3,
    Registry = 4,
    File = 5,
    Folder = 6,
    Other = 7
}

public enum ChangeKind
{
    Added = 0,
    Removed = 1,
    Modified = 2,
    Unchanged = 3,
    Paused = 4,
    Restored = 5
}

public enum ConfidenceLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum ImportanceLevel
{
    Normal = 0,
    Check = 1,
    Attention = 2
}

public enum ScanSection
{
    Applications = 0,
    Startup = 1,
    Services = 2,
    ScheduledTasks = 3,
    Files = 4
}

public enum SectionStatus
{
    Complete = 0,
    Partial = 1,
    Unavailable = 2
}

public enum ScanReason
{
    Baseline = 0,
    Manual = 1,
    Monitoring = 2,
    TrackingResult = 3
}

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum AppLanguage
{
    System = 0,
    Russian = 1,
    English = 2
}

public enum AppStatus
{
    Unchanged = 0,
    Updated = 1,
    Changed = 2,
    Attention = 3,
    PausedComponents = 4,
    New = 5
}

public enum PauseStatus
{
    Active = 0,
    Restored = 1,
    Conflict = 2,
    Failed = 3,
    TargetMissing = 4,
    KeptCurrent = 5
}

public enum PauseTargetKind
{
    RegistryStartup = 0,
    StartupFolder = 1,
    ScheduledTask = 2
}

public enum RestoreOutcome
{
    Restored = 0,
    Conflict = 1,
    Missing = 2,
    Failed = 3,
    RequiresElevation = 4
}

public enum TrackingSessionStatus
{
    ScanningBaseline = 0,
    AwaitingInstall = 1,
    ScanningResult = 2,
    Completed = 3,
    Cancelled = 4,
    Interrupted = 5
}

public enum SearchResultKind
{
    Application = 0,
    Event = 1,
    Component = 2,
    Section = 3
}

public enum NotificationSeverity
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
