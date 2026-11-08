using SunTrace.Core.Enums;

namespace SunTrace.Core.Models;

public sealed class SnapshotDiff
{
    public List<AppChange> AppChanges { get; } = new();

    public List<ComponentChange> ComponentChanges { get; } = new();

    public bool HasChanges => AppChanges.Count > 0 || ComponentChanges.Count > 0;

    public int AddedCount => AppChanges.Count(a => a.Kind == ChangeKind.Added) + ComponentChanges.Count(c => c.Kind == ChangeKind.Added);

    public int RemovedCount => AppChanges.Count(a => a.Kind == ChangeKind.Removed) + ComponentChanges.Count(c => c.Kind == ChangeKind.Removed);

    public int ModifiedCount => AppChanges.Count(a => a.Kind == ChangeKind.Modified) + ComponentChanges.Count(c => c.Kind == ChangeKind.Modified);
}

public sealed class AppChange
{
    public ChangeKind Kind { get; init; }

    public string AppId { get; init; } = string.Empty;

    public InstalledApp? Before { get; init; }

    public InstalledApp? After { get; init; }

    public bool IsUpdate { get; init; }

    public List<FieldPair> Fields { get; init; } = new();

    public string AppName => After?.Name ?? Before?.Name ?? "Неизвестная программа";
}

public sealed class ComponentChange
{
    public ChangeKind Kind { get; init; }

    public ComponentType Type { get; init; }

    public string Key { get; init; } = string.Empty;

    public ComponentSnapshot? Before { get; init; }

    public ComponentSnapshot? After { get; init; }

    public List<FieldPair> Fields { get; init; } = new();

    public string DisplayName => After?.Name ?? Before?.Name ?? "Компонент";
}

public sealed class FieldPair
{
    public string Label { get; init; } = string.Empty;

    public string? Before { get; init; }

    public string? After { get; init; }
}
