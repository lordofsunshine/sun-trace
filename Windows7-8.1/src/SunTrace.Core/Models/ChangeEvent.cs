using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class ChangeEvent
{
    public long Id { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public string? AppId { get; set; }

    public string AppName { get; set; } = string.Empty;

    public ChangeKind Kind { get; set; }

    public ComponentType ComponentType { get; set; }

    public string ComponentName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public EventDetails Details { get; set; } = new();

    public ConfidenceLevel Confidence { get; set; }

    public List<string> ConfidenceReasons { get; set; } = new();

    public ImportanceLevel Importance { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; }

    public long? SnapshotId { get; set; }

    public long? SessionId { get; set; }

    public bool IsReverted { get; set; }

    public bool IsRead { get; set; }

    public int AffectedCount { get; set; } = 1;

    public bool HasMultipleChanges => AffectedCount > 1 || Details.Fields.Count > 1 || Details.SamplePaths.Count > 1;

    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "Добавлено",
        ChangeKind.Removed => "Удалено",
        ChangeKind.Modified => "Изменено",
        ChangeKind.Paused => "Приостановлено",
        ChangeKind.Restored => "Восстановлено",
        _ => "Без изменений"
    };

    public string ComponentTypeLabel => ComponentType switch
    {
        ComponentType.Application => "Программа",
        ComponentType.Startup => "Автозагрузка",
        ComponentType.Service => "Служба",
        ComponentType.ScheduledTask => "Задание планировщика",
        ComponentType.Registry => "Реестр",
        ComponentType.File => "Файлы",
        ComponentType.Folder => "Папки",
        _ => "Компонент"
    };
}

public sealed class EventDetails
{
    public List<FieldPair> Fields { get; set; } = new();

    public Dictionary<string, string> Technical { get; set; } = new();

    public string? Explanation { get; set; }

    public string? Effect { get; set; }

    public string? ComponentDetail { get; set; }

    public string? ImportanceNote { get; set; }

    public List<string> SamplePaths { get; set; } = new();
}

public sealed class EventQuery
{
    public string? SearchTerm { get; init; }

    public string? AppId { get; init; }

    public ComponentType? ComponentType { get; init; }

    public ImportanceLevel? Importance { get; init; }

    public ChangeKind? Kind { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int PageSize { get; init; } = 30;

    public int Offset { get; init; }

    public long? SnapshotId { get; init; }
}

public sealed class EventPage
{
    public IReadOnlyList<ChangeEvent> Items { get; init; } = Array.Empty<ChangeEvent>();

    public int TotalCount { get; init; }

    public bool HasMore => Offset + Items.Count < TotalCount;

    public int Offset { get; init; }
}
